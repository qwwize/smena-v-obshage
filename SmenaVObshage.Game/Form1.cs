namespace SmenaVObshage.Game;

public partial class Form1 : Form
{
    // Настройки задач протечка 2 сек спавн каждые 240 тиков лимит 6 задач
    private const float LeakHoldMs = 2000f;
    private const int SpawnIntervalTicks = 240;
    private const int MaxActiveAndQueued = 6;

    private readonly System.Windows.Forms.Timer _gameTimer = new();
    private readonly HashSet<Keys> _pressedKeys = [];
    private readonly List<GameTask> _taskQueue = [];
    private readonly Random _random = new();

    private readonly Rectangle _playField = new(20, 20, 760, 380);
    private readonly Size _playerSize = new(24, 24);
    private PointF _playerPosition = new(60, 60);
    private const float PlayerSpeedPerTick = 4f;

    // Текущее состояние игры: активная задача, очередь, счет и статус
    private GameTask? _currentTask;
    private int _spawnCooldownTicks;
    private bool _eWasDown;
    private int _completedCount;
    private string _statusMessage = string.Empty;
    private int _statusTicksRemaining;
    private readonly Dictionary<Guid, DateTime> _taskStartUtc = [];

    public Form1()
    {
        InitializeComponent();
        Text = "Смена в общаге - MVP";
        DoubleBuffered = true;
        KeyPreview = true;

        _gameTimer.Interval = 16;
        _gameTimer.Tick += GameTimer_Tick;

        Paint += Form1_Paint;
        KeyDown += Form1_KeyDown;
        KeyUp += Form1_KeyUp;
        Shown += (_, _) =>
        {
            SeedInitialTasks();
            _gameTimer.Start();
        };
    }

    private void SeedInitialTasks()
    {
        // Стартовые задачи, чтобы игра начиналась не с пустого поля
        EnqueueTask(CreateTask(GameEventKind.Delivery));
        EnqueueTask(CreateTask(GameEventKind.Breakers));
        EnsureCurrentTask();
    }

    private void GameTimer_Tick(object? sender, EventArgs e)
    {
        // Ввод и движение
        var dx = 0f;
        var dy = 0f;

        if (_pressedKeys.Contains(Keys.A) || _pressedKeys.Contains(Keys.Left))
        {
            dx -= PlayerSpeedPerTick;
        }

        if (_pressedKeys.Contains(Keys.D) || _pressedKeys.Contains(Keys.Right))
        {
            dx += PlayerSpeedPerTick;
        }

        if (_pressedKeys.Contains(Keys.W) || _pressedKeys.Contains(Keys.Up))
        {
            dy -= PlayerSpeedPerTick;
        }

        if (_pressedKeys.Contains(Keys.S) || _pressedKeys.Contains(Keys.Down))
        {
            dy += PlayerSpeedPerTick;
        }

        _playerPosition.X += dx;
        _playerPosition.Y += dy;

        ClampPlayerToField();

        // Короткие сообщения успеха и провала живут ограниченное время
        if (_statusTicksRemaining > 0)
        {
            _statusTicksRemaining--;
            if (_statusTicksRemaining == 0)
            {
                _statusMessage = string.Empty;
            }
        }

        var eNow = _pressedKeys.Contains(Keys.E);
        var ePressedThisFrame = eNow && !_eWasDown;
        _eWasDown = eNow;

        // Обновление задач
        EnsureCurrentTask();
        TrySpawnTask();
        UpdateCurrentTask(eNow, ePressedThisFrame);

        // Перерисовка кадра
        Invalidate();
    }

    private void EnsureCurrentTask()
    {
        // Если активной задачи нет берем следующую из очереди
        if (_currentTask != null)
        {
            return;
        }

        if (_taskQueue.Count == 0)
        {
            return;
        }

        _currentTask = _taskQueue[0];
        _taskQueue.RemoveAt(0);
    }

    private void TrySpawnTask()
    {
        // Спавним периодически, но ограничиваем общее количество задач
        var total = (_currentTask != null ? 1 : 0) + _taskQueue.Count;
        if (total >= MaxActiveAndQueued)
        {
            return;
        }

        if (++_spawnCooldownTicks < SpawnIntervalTicks)
        {
            return;
        }

        _spawnCooldownTicks = 0;
        EnqueueTask(CreateTask(PickRandomKind()));
        EnsureCurrentTask();
    }

    private void UpdateCurrentTask(bool eHeld, bool ePressedThisFrame)
    {
        // Проверка выполнения и провала активной задачи по ее правилам
        var task = _currentTask;
        if (task == null)
        {
            return;
        }

        if (task.TimeLimitSeconds > 0f)
        {
            var elapsed = (float)(DateTime.UtcNow - GetTaskStartUtc(task)).TotalSeconds;
            if (elapsed > task.TimeLimitSeconds)
            {
                FailCurrentTask("Время проверки вышло!");
                return;
            }
        }

        var inZone = PlayerInZone(task.Zone);

        switch (task.Kind)
        {
            case GameEventKind.Delivery:
                if (inZone)
                {
                    CompleteCurrentTask();
                }

                break;

            case GameEventKind.Leak:
                if (inZone && eHeld)
                {
                    task.HoldProgressMs += _gameTimer.Interval;
                    if (task.HoldProgressMs >= LeakHoldMs)
                    {
                        CompleteCurrentTask();
                    }
                }
                else
                {
                    task.HoldProgressMs = 0f;
                }

                break;

            case GameEventKind.Breakers:
            case GameEventKind.NoisyNeighbors:
            case GameEventKind.CommandantCheck:
                if (inZone && ePressedThisFrame)
                {
                    CompleteCurrentTask();
                }

                break;
        }
    }

    private DateTime GetTaskStartUtc(GameTask task)
    {
        // Сохраняем время начала задачи чтобы проверять лимит по времени
        if (!_taskStartUtc.TryGetValue(task.Id, out var t))
        {
            t = DateTime.UtcNow;
            _taskStartUtc[task.Id] = t;
        }

        return t;
    }

    private void CompleteCurrentTask()
    {
        // Успешное завершение и переход к следующей задаче
        if (_currentTask != null)
        {
            _taskStartUtc.Remove(_currentTask.Id);
        }

        _completedCount++;
        _statusMessage = "Задача выполнена";
        _statusTicksRemaining = 115;
        _currentTask = null;
        EnsureCurrentTask();
    }

    private void FailCurrentTask(string message)
    {
        // Провал задачи, например, если истек таймер срочной
        if (_currentTask != null)
        {
            _taskStartUtc.Remove(_currentTask.Id);
        }

        _statusMessage = message;
        _statusTicksRemaining = 160;
        _currentTask = null;
        EnsureCurrentTask();
    }

    private bool PlayerInZone(RectangleF zone)
    {
        // Считаем попадание по центру игрока
        var cx = _playerPosition.X + _playerSize.Width / 2f;
        var cy = _playerPosition.Y + _playerSize.Height / 2f;
        return zone.Contains(cx, cy);
    }

    private void EnqueueTask(GameTask task)
    {
        // Срочные задачи ставим раньше обычных
        if (!task.IsUrgent)
        {
            _taskQueue.Add(task);
            return;
        }

        var i = 0;
        while (i < _taskQueue.Count && _taskQueue[i].IsUrgent)
        {
            i++;
        }

        _taskQueue.Insert(i, task);
    }

    private GameEventKind PickRandomKind()
    {
        // Небольшой шанс на срочную задачу коменданта
        if (_random.Next(100) < 14)
        {
            return GameEventKind.CommandantCheck;
        }

        GameEventKind[] normal =
        [
            GameEventKind.Leak,
            GameEventKind.Breakers,
            GameEventKind.Delivery,
            GameEventKind.NoisyNeighbors,
        ];

        return normal[_random.Next(normal.Length)];
    }

    private GameTask CreateTask(GameEventKind kind)
    {
        // Параметры и зона зависят от типа задачи
        return kind switch
        {
            GameEventKind.Leak => new GameTask
            {
                Kind = kind,
                Zone = RandomPointZone(44f),
                IsUrgent = false,
                TimeLimitSeconds = 0f,
            },
            GameEventKind.Breakers => new GameTask
            {
                Kind = kind,
                Zone = RandomPointZone(40f),
                IsUrgent = false,
                TimeLimitSeconds = 0f,
            },
            GameEventKind.Delivery => new GameTask
            {
                Kind = kind,
                Zone = RandomRoomZone(118f, 88f),
                IsUrgent = false,
                TimeLimitSeconds = 0f,
            },
            GameEventKind.NoisyNeighbors => new GameTask
            {
                Kind = kind,
                Zone = RandomRoomZone(102f, 78f),
                IsUrgent = false,
                TimeLimitSeconds = 0f,
            },
            GameEventKind.CommandantCheck => new GameTask
            {
                Kind = kind,
                Zone = RandomPointZone(38f),
                IsUrgent = true,
                TimeLimitSeconds = 18f,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private RectangleF RandomRoomZone(float width, float height)
    {
        // Прямоугольная зона комнаты внутри игрового поля
        var margin = 10f;
        var minX = (int)MathF.Ceiling(_playField.Left + margin);
        var minY = (int)MathF.Ceiling(_playField.Top + margin);
        var maxX = (int)MathF.Floor(_playField.Right - width - margin);
        var maxY = (int)MathF.Floor(_playField.Bottom - height - margin);
        if (maxX <= minX)
        {
            maxX = minX + 1;
        }

        if (maxY <= minY)
        {
            maxY = minY + 1;
        }

        var x = _random.Next(minX, maxX);
        var y = _random.Next(minY, maxY);
        return new RectangleF(x, y, width, height);
    }

    private RectangleF RandomPointZone(float size)
    {
        // Компактная зона вокруг случайной точки внутри поля
        var half = size / 2f;
        var minCx = (int)MathF.Ceiling(_playField.Left + half + 8f);
        var minCy = (int)MathF.Ceiling(_playField.Top + half + 8f);
        var maxCx = (int)MathF.Floor(_playField.Right - half - 8f);
        var maxCy = (int)MathF.Floor(_playField.Bottom - half - 8f);
        if (maxCx <= minCx)
        {
            maxCx = minCx + 1;
        }

        if (maxCy <= minCy)
        {
            maxCy = minCy + 1;
        }

        var cx = _random.Next(minCx, maxCx);
        var cy = _random.Next(minCy, maxCy);
        return new RectangleF(cx - half, cy - half, size, size);
    }

    private void ClampPlayerToField()
    {
        // Не даем игроку выйти за границы поля
        var minX = _playField.Left;
        var minY = _playField.Top;
        var maxX = _playField.Right - _playerSize.Width;
        var maxY = _playField.Bottom - _playerSize.Height;

        _playerPosition.X = Math.Clamp(_playerPosition.X, minX, maxX);
        _playerPosition.Y = Math.Clamp(_playerPosition.Y, minY, maxY);
    }

    private void Form1_KeyDown(object? sender, KeyEventArgs e)
    {
        _pressedKeys.Add(e.KeyCode);
    }

    private void Form1_KeyUp(object? sender, KeyEventArgs e)
    {
        _pressedKeys.Remove(e.KeyCode);
    }

    private static string KindTitle(GameEventKind kind) => kind switch
    {
        GameEventKind.Leak => "Протечка",
        GameEventKind.Breakers => "Пробки",
        GameEventKind.Delivery => "Доставка",
        GameEventKind.NoisyNeighbors => "Шум",
        GameEventKind.CommandantCheck => "Комендант!",
        _ => kind.ToString(),
    };

    private static string KindHint(GameEventKind kind) => kind switch
    {
        GameEventKind.Leak => "удерживайте E",
        GameEventKind.Breakers => "нажмите E",
        GameEventKind.Delivery => "войдите в комнату",
        GameEventKind.NoisyNeighbors => "нажмите E в комнате",
        GameEventKind.CommandantCheck => "срочно: E в зоне",
        _ => string.Empty,
    };

    private void Form1_Paint(object? sender, PaintEventArgs e)
    {
        // Отрисовка поля активной задачи игрока и HUD
        var g = e.Graphics;
        g.Clear(Color.FromArgb(30, 30, 34));

        using var fieldBrush = new SolidBrush(Color.FromArgb(50, 52, 58));
        using var fieldBorderPen = new Pen(Color.FromArgb(120, 125, 140), 2);
        g.FillRectangle(fieldBrush, _playField);
        g.DrawRectangle(fieldBorderPen, _playField);

        if (_currentTask != null)
        {
            var t = _currentTask;
            using var curBrush = new SolidBrush(Color.FromArgb(70, t.IsUrgent ? 255 : 90, t.IsUrgent ? 140 : 195, 100));
            g.FillRectangle(curBrush, t.Zone);
            using var curPen = new Pen(t.IsUrgent ? Color.OrangeRed : Color.FromArgb(85, 195, 255), 2);
            g.DrawRectangle(curPen, t.Zone.X, t.Zone.Y, t.Zone.Width, t.Zone.Height);
        }

        var playerRect = new RectangleF(_playerPosition, _playerSize);
        using var playerBrush = new SolidBrush(Color.FromArgb(85, 195, 255));
        g.FillEllipse(playerBrush, playerRect);

        using var hudBrush = new SolidBrush(Color.WhiteSmoke);
        using var hudFont = new Font("Segoe UI", 10, FontStyle.Regular);
        using var hudBold = new Font("Segoe UI", 10, FontStyle.Bold);

        g.DrawString("Движение: WASD / Стрелки  ·  Действие: E", hudFont, hudBrush, 20, 408);
        g.DrawString($"Выполнено: {_completedCount}  ·  Tick: {_gameTimer.Interval} ms", hudFont, hudBrush, 420, 408);

        var hudX = 20;
        var hudY = 428;
        if (_currentTask != null)
        {
            var cur = _currentTask;
            var line = $"Сейчас: {KindTitle(cur.Kind)} ({KindHint(cur.Kind)})";
            if (cur.Kind == GameEventKind.Leak)
            {
                var p = Math.Clamp(cur.HoldProgressMs / LeakHoldMs, 0f, 1f);
                line += $" — {p:P0}";
            }

            if (cur.TimeLimitSeconds > 0f)
            {
                var left = cur.TimeLimitSeconds - (float)(DateTime.UtcNow - GetTaskStartUtc(cur)).TotalSeconds;
                line += $"  ·  осталось {MathF.Max(0f, left):0.0} с";
            }

            g.DrawString(line, cur.IsUrgent ? hudBold : hudFont, cur.IsUrgent ? Brushes.OrangeRed : hudBrush, hudX, hudY);
            hudY += 18;
        }
        else
        {
            g.DrawString("Сейчас: нет активной задачи (ждите новую)", hudFont, hudBrush, hudX, hudY);
            hudY += 18;
        }

        g.DrawString("Очередь (ближайшие):", hudFont, hudBrush, hudX, hudY);
        hudY += 16;

        var show = Math.Min(3, _taskQueue.Count);
        if (show == 0)
        {
            g.DrawString("  (пусто)", hudFont, new SolidBrush(Color.Gray), hudX, hudY);
            hudY += 16;
        }
        else
        {
            for (var i = 0; i < show; i++)
            {
                var t = _taskQueue[i];
                var urgent = t.IsUrgent ? "[СРОЧНО] " : string.Empty;
                g.DrawString($"  {i + 1}. {urgent}{KindTitle(t.Kind)}", t.IsUrgent ? hudBold : hudFont,
                    t.IsUrgent ? Brushes.OrangeRed : hudBrush, hudX, hudY);
                hudY += 16;
            }
        }

        if (!string.IsNullOrEmpty(_statusMessage))
        {
            g.DrawString(_statusMessage, hudFont, Brushes.Khaki, 420, 428);
        }
    }
}
