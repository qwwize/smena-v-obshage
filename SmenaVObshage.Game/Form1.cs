namespace SmenaVObshage.Game;

public partial class Form1 : Form
{
    // Настройки задач протечка 2 сек спавн каждые 240 тиков лимит 6 задач
    private const float LeakHoldMs = 2000f;
    private const int SpawnIntervalTicks = 240;
    private const int MaxActiveAndQueued = 6;
    private const float SessionDurationSeconds = 180f;
    private const int TargetCompletedTasks = 14;
    private const float ChaosPassivePerSecond = 1.2f;
    private const float ChaosPenaltyOnFail = 14f;
    private const float ChaosReduceOnComplete = 7f;

    private readonly System.Windows.Forms.Timer _gameTimer = new();
    private readonly HashSet<Keys> _pressedKeys = [];
    private readonly List<GameTask> _taskQueue = [];
    private readonly Random _random = new();
    private readonly List<RectangleF> _wallSegments = [];
    private readonly List<RectangleF> _solidBlocks = [];

    private readonly Rectangle _playField = new(20, 20, 940, 420);
    private readonly Size _playerSize = new(24, 24);
    private PointF _playerPosition = new(60, 60);
    private const float PlayerSpeedPerTick = 4f;

    // Текущее состояние игры: активная задача, очередь, счет и статус
    private GameTask? _currentTask;
    private int _spawnCooldownTicks;
    private bool _eWasDown;
    private int _completedCount;
    private float _sessionTimeLeftSeconds = SessionDurationSeconds;
    private float _chaosLevel;
    private bool _isPaused;
    private bool _isGameOver;
    private bool _isVictory;
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

    private void ResetGame()
    {
        _playerPosition = new PointF(72, 72);
        _taskQueue.Clear();
        _wallSegments.Clear();
        _solidBlocks.Clear();
        _taskStartUtc.Clear();
        _currentTask = null;
        _spawnCooldownTicks = 0;
        _eWasDown = false;
        _completedCount = 0;
        _chaosLevel = 0f;
        _sessionTimeLeftSeconds = SessionDurationSeconds;
        _isPaused = false;
        _isGameOver = false;
        _isVictory = false;
        _statusMessage = "Новая смена началась";
        _statusTicksRemaining = 100;
        SeedInitialTasks();
    }

    private void GameTimer_Tick(object? sender, EventArgs e)
    {
        if (_isPaused || _isGameOver)
        {
            Invalidate();
            return;
        }

        var dtSeconds = _gameTimer.Interval / 1000f;
        _sessionTimeLeftSeconds = Math.Max(0f, _sessionTimeLeftSeconds - dtSeconds);
        _chaosLevel = Math.Clamp(_chaosLevel + ChaosPassivePerSecond * dtSeconds, 0f, 100f);

        if (_sessionTimeLeftSeconds <= 0f)
        {
            FinishByTime();
            Invalidate();
            return;
        }

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

        MovePlayerWithCollision(dx, dy);

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

        var nextTask = _taskQueue[0];
        _taskQueue.RemoveAt(0);
        GenerateObstacleLayout();
        _currentTask = RebuildTaskForCurrentLayout(nextTask);
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
        _chaosLevel = Math.Clamp(_chaosLevel - ChaosReduceOnComplete, 0f, 100f);
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

        _chaosLevel = Math.Clamp(_chaosLevel + ChaosPenaltyOnFail, 0f, 100f);
        _statusMessage = message;
        _statusTicksRemaining = 160;
        _currentTask = null;
        EnsureCurrentTask();
    }

    private void FinishByTime()
    {
        _isGameOver = true;
        _isVictory = _completedCount >= TargetCompletedTasks && _chaosLevel < 80f;
        _statusMessage = _isVictory
            ? "Смена успешно завершена"
            : "Смена окончена неудачей";
        _statusTicksRemaining = 0;
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

        for (var i = 0; i < 120; i++)
        {
            var x = _random.Next(minX, maxX);
            var y = _random.Next(minY, maxY);
            var zone = new RectangleF(x, y, width, height);
            if (IsZoneWalkable(zone))
            {
                return zone;
            }
        }

        return new RectangleF(_playField.Left + 20, _playField.Top + 20, width, height);
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

        for (var i = 0; i < 120; i++)
        {
            var cx = _random.Next(minCx, maxCx);
            var cy = _random.Next(minCy, maxCy);
            var zone = new RectangleF(cx - half, cy - half, size, size);
            if (IsZoneWalkable(zone))
            {
                return zone;
            }
        }

        return new RectangleF(_playField.Left + 30, _playField.Top + 30, size, size);
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

    private void MovePlayerWithCollision(float dx, float dy)
    {
        var xCandidate = new PointF(_playerPosition.X + dx, _playerPosition.Y);
        ClampPointToField(ref xCandidate);
        if (!IntersectsObstacles(GetPlayerRect(xCandidate)))
        {
            _playerPosition = xCandidate;
        }

        var yCandidate = new PointF(_playerPosition.X, _playerPosition.Y + dy);
        ClampPointToField(ref yCandidate);
        if (!IntersectsObstacles(GetPlayerRect(yCandidate)))
        {
            _playerPosition = yCandidate;
        }
    }

    private void ClampPointToField(ref PointF p)
    {
        p.X = Math.Clamp(p.X, _playField.Left, _playField.Right - _playerSize.Width);
        p.Y = Math.Clamp(p.Y, _playField.Top, _playField.Bottom - _playerSize.Height);
    }

    private RectangleF GetPlayerRect(PointF p) => new(p.X, p.Y, _playerSize.Width, _playerSize.Height);

    private bool IntersectsObstacles(RectangleF rect)
    {
        foreach (var wall in _wallSegments)
        {
            if (rect.IntersectsWith(wall))
            {
                return true;
            }
        }

        foreach (var block in _solidBlocks)
        {
            if (rect.IntersectsWith(block))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsZoneWalkable(RectangleF zone)
    {
        foreach (var wall in _wallSegments)
        {
            if (zone.IntersectsWith(wall))
            {
                return false;
            }
        }

        foreach (var block in _solidBlocks)
        {
            if (zone.IntersectsWith(block))
            {
                return false;
            }
        }

        return true;
    }

    private void GenerateObstacleLayout()
    {
        _wallSegments.Clear();
        _solidBlocks.Clear();

        var t = 10f;
        var minX = _playField.Left + 26;
        var maxX = _playField.Right - 26;
        var minY = _playField.Top + 26;
        var maxY = _playField.Bottom - 26;

        AddHorizontalWall(minY + 88, minX, maxX, t);
        AddHorizontalWall(minY + 188, minX, maxX, t);
        AddVerticalWall(minX + 220, minY, maxY, t);
        AddVerticalWall(minX + 480, minY, maxY, t);

        for (var i = 0; i < 4; i++)
        {
            var w = _random.Next(56, 86);
            var h = _random.Next(42, 72);
            var x = _random.Next((int)minX + 20, (int)maxX - w - 20);
            var y = _random.Next((int)minY + 20, (int)maxY - h - 20);
            var candidate = new RectangleF(x, y, w, h);
            if (!IntersectsAny(candidate))
            {
                _solidBlocks.Add(candidate);
            }
        }

        EnsurePlayerOutsideObstacles();
    }

    private void AddHorizontalWall(float y, float x1, float x2, float t)
    {
        var gapW = _random.Next(95, 145);
        var gapX = _random.Next((int)x1 + 60, (int)x2 - gapW - 60);
        var left = new RectangleF(x1, y, gapX - x1, t);
        var right = new RectangleF(gapX + gapW, y, x2 - (gapX + gapW), t);
        if (left.Width > 20)
        {
            _wallSegments.Add(left);
        }
        if (right.Width > 20)
        {
            _wallSegments.Add(right);
        }
    }

    private void AddVerticalWall(float x, float y1, float y2, float t)
    {
        var gapH = _random.Next(82, 132);
        var gapY = _random.Next((int)y1 + 50, (int)y2 - gapH - 50);
        var top = new RectangleF(x, y1, t, gapY - y1);
        var bottom = new RectangleF(x, gapY + gapH, t, y2 - (gapY + gapH));
        if (top.Height > 20)
        {
            _wallSegments.Add(top);
        }
        if (bottom.Height > 20)
        {
            _wallSegments.Add(bottom);
        }
    }

    private bool IntersectsAny(RectangleF rect)
    {
        foreach (var wall in _wallSegments)
        {
            if (rect.IntersectsWith(wall))
            {
                return true;
            }
        }

        foreach (var block in _solidBlocks)
        {
            if (rect.IntersectsWith(block))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsurePlayerOutsideObstacles()
    {
        if (!IntersectsObstacles(GetPlayerRect(_playerPosition)))
        {
            return;
        }

        for (var i = 0; i < 120; i++)
        {
            var x = _random.Next(_playField.Left + 12, _playField.Right - _playerSize.Width - 12);
            var y = _random.Next(_playField.Top + 12, _playField.Bottom - _playerSize.Height - 12);
            var candidate = new PointF(x, y);
            if (!IntersectsObstacles(GetPlayerRect(candidate)))
            {
                _playerPosition = candidate;
                return;
            }
        }

        _playerPosition = new PointF(_playField.Left + 14, _playField.Top + 14);
    }

    private GameTask RebuildTaskForCurrentLayout(GameTask task)
    {
        var zone = task.Kind switch
        {
            GameEventKind.Delivery => RandomRoomZone(118f, 88f),
            GameEventKind.NoisyNeighbors => RandomRoomZone(102f, 78f),
            GameEventKind.Leak => RandomPointZone(44f),
            GameEventKind.Breakers => RandomPointZone(40f),
            GameEventKind.CommandantCheck => RandomPointZone(38f),
            _ => RandomPointZone(40f),
        };

        return new GameTask
        {
            Kind = task.Kind,
            Zone = zone,
            IsUrgent = task.IsUrgent,
            TimeLimitSeconds = task.TimeLimitSeconds,
            HoldProgressMs = 0f,
        };
    }

    private void Form1_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            if (_isGameOver)
            {
                Close();
                return;
            }

            _isPaused = !_isPaused;
            _statusMessage = _isPaused ? "Пауза" : "Продолжение смены";
            _statusTicksRemaining = 80;
            Invalidate();
            return;
        }

        if (e.KeyCode == Keys.R)
        {
            ResetGame();
            Invalidate();
            return;
        }

        if (e.KeyCode == Keys.Q)
        {
            Close();
            return;
        }

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

    private void DrawDormBuildings(Graphics g)
    {
        using var wallBrush = new SolidBrush(Color.FromArgb(78, 94, 118));
        using var wallPen = new Pen(Color.FromArgb(140, 168, 196), 1);
        using var blockBrush = new SolidBrush(Color.FromArgb(70, 76, 92));
        using var blockPen = new Pen(Color.FromArgb(136, 150, 176), 2);
        using var dotBrush = new SolidBrush(Color.FromArgb(150, 180, 120));

        foreach (var wall in _wallSegments)
        {
            g.FillRectangle(wallBrush, wall);
            g.DrawRectangle(wallPen, wall.X, wall.Y, wall.Width, wall.Height);
        }

        foreach (var block in _solidBlocks)
        {
            g.FillRectangle(blockBrush, block);
            g.DrawRectangle(blockPen, block.X, block.Y, block.Width, block.Height);

            var cols = Math.Max(2, (int)(block.Width / 36f));
            var rows = Math.Max(2, (int)(block.Height / 30f));
            var gapX = block.Width / (cols + 1f);
            var gapY = block.Height / (rows + 1f);
            for (var r = 1; r <= rows; r++)
            {
                for (var c = 1; c <= cols; c++)
                {
                    var wx = block.X + c * gapX - 3;
                    var wy = block.Y + r * gapY - 3;
                    g.FillRectangle(dotBrush, wx, wy, 6, 6);
                }
            }
        }
    }

    private void DrawPlayer(Graphics g)
    {
        var playerRect = new RectangleF(_playerPosition, _playerSize);
        using var shadowBrush = new SolidBrush(Color.FromArgb(85, 18, 24, 35));
        g.FillEllipse(shadowBrush, playerRect.X + 2, playerRect.Y + 3, playerRect.Width, playerRect.Height);

        using var bodyBrush = new SolidBrush(Color.FromArgb(86, 195, 255));
        using var bodyPen = new Pen(Color.FromArgb(218, 236, 252), 2);
        g.FillEllipse(bodyBrush, playerRect);
        g.DrawEllipse(bodyPen, playerRect);

        using var eyeBrush = new SolidBrush(Color.FromArgb(16, 32, 55));
        g.FillEllipse(eyeBrush, playerRect.X + 7, playerRect.Y + 8, 3, 3);
        g.FillEllipse(eyeBrush, playerRect.X + 14, playerRect.Y + 8, 3, 3);
    }

    private void DrawTaskMarker(Graphics g, GameTask task)
    {
        var color = task.IsUrgent ? Color.FromArgb(250, 108, 80) : Color.FromArgb(98, 214, 255);
        using var zoneFill = new SolidBrush(Color.FromArgb(55, color));
        using var zonePen = new Pen(color, task.IsUrgent ? 3 : 2);
        g.FillRectangle(zoneFill, task.Zone);
        g.DrawRectangle(zonePen, task.Zone.X, task.Zone.Y, task.Zone.Width, task.Zone.Height);
    }

    private static void DrawCard(Graphics g, RectangleF rect)
    {
        using var cardBrush = new SolidBrush(Color.FromArgb(42, 46, 58));
        using var cardBorder = new Pen(Color.FromArgb(108, 122, 148), 1);
        g.FillRectangle(cardBrush, rect);
        g.DrawRectangle(cardBorder, rect.X, rect.Y, rect.Width, rect.Height);
    }

    private void DrawHud(Graphics g)
    {
        var hudTop = _playField.Bottom + 16;
        var controlsCard = new RectangleF(20, hudTop, 295, 220);
        var tasksCard = new RectangleF(335, hudTop, 305, 220);
        var statsCard = new RectangleF(660, hudTop, 300, 220);

        DrawCard(g, controlsCard);
        DrawCard(g, tasksCard);
        DrawCard(g, statsCard);

        using var text = new SolidBrush(Color.FromArgb(238, 244, 255));
        using var subText = new SolidBrush(Color.FromArgb(165, 178, 198));
        using var titleFont = new Font("Segoe UI", 10, FontStyle.Bold);
        using var textFont = new Font("Segoe UI", 10, FontStyle.Regular);

        g.DrawString("Управление", titleFont, text, controlsCard.X + 12, controlsCard.Y + 10);
        g.DrawString("WASD или стрелки  движение", textFont, text, controlsCard.X + 12, controlsCard.Y + 38);
        g.DrawString("E  взаимодействие", textFont, text, controlsCard.X + 12, controlsCard.Y + 58);
        g.DrawString("Esc  пауза", textFont, text, controlsCard.X + 12, controlsCard.Y + 78);
        g.DrawString("R  рестарт", textFont, text, controlsCard.X + 12, controlsCard.Y + 98);
        g.DrawString("Q  выход", textFont, text, controlsCard.X + 12, controlsCard.Y + 118);
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            g.DrawString("Статус", titleFont, text, controlsCard.X + 12, controlsCard.Y + 154);
            g.DrawString(_statusMessage, textFont, Brushes.Khaki, controlsCard.X + 12, controlsCard.Y + 178);
        }

        g.DrawString("Задачи", titleFont, text, tasksCard.X + 12, tasksCard.Y + 10);
        var rowY = tasksCard.Y + 38;
        if (_currentTask != null)
        {
            var cur = _currentTask;
            var currentLine = $"Сейчас  {KindTitle(cur.Kind)}";
            if (cur.TimeLimitSeconds > 0f)
            {
                var left = cur.TimeLimitSeconds - (float)(DateTime.UtcNow - GetTaskStartUtc(cur)).TotalSeconds;
                currentLine += $"  ({MathF.Max(0f, left):0.0}с)";
            }
            g.DrawString(currentLine, titleFont, cur.IsUrgent ? Brushes.OrangeRed : text, tasksCard.X + 12, rowY);
            rowY += 24;
            g.DrawString(KindHint(cur.Kind), textFont, subText, tasksCard.X + 12, rowY);
            rowY += 26;
        }
        else
        {
            g.DrawString("Сейчас нет активной задачи", textFont, subText, tasksCard.X + 12, rowY);
            rowY += 30;
        }

        g.DrawString("Очередь", titleFont, text, tasksCard.X + 12, rowY);
        rowY += 22;
        var show = Math.Min(4, _taskQueue.Count);
        if (show == 0)
        {
            g.DrawString("Пусто", textFont, subText, tasksCard.X + 12, rowY);
        }
        else
        {
            for (var i = 0; i < show; i++)
            {
                var task = _taskQueue[i];
                var label = $"{i + 1}. {(task.IsUrgent ? "[СРОЧНО] " : string.Empty)}{KindTitle(task.Kind)}";
                g.DrawString(label, textFont, task.IsUrgent ? Brushes.OrangeRed : text, tasksCard.X + 12, rowY);
                rowY += 20;
            }
        }

        g.DrawString("Показатели смены", titleFont, text, statsCard.X + 12, statsCard.Y + 10);
        var timeLeft = TimeSpan.FromSeconds(MathF.Max(0f, _sessionTimeLeftSeconds));
        g.DrawString($"Время  {timeLeft.Minutes:00}:{timeLeft.Seconds:00}", titleFont, text, statsCard.X + 12, statsCard.Y + 40);
        g.DrawString($"Счет  {_completedCount}/{TargetCompletedTasks}", textFont, text, statsCard.X + 12, statsCard.Y + 66);

        var chaosBarRect = new RectangleF(statsCard.X + 12, statsCard.Y + 102, statsCard.Width - 24, 18);
        using var chaosBack = new SolidBrush(Color.FromArgb(66, 74, 92));
        g.FillRectangle(chaosBack, chaosBarRect);
        var chaosColor = _chaosLevel < 40f
            ? Color.FromArgb(81, 183, 126)
            : _chaosLevel < 70f
                ? Color.FromArgb(228, 176, 74)
                : Color.FromArgb(224, 85, 85);
        using var chaosFill = new SolidBrush(chaosColor);
        var fillWidth = chaosBarRect.Width * (_chaosLevel / 100f);
        g.FillRectangle(chaosFill, chaosBarRect.X, chaosBarRect.Y, fillWidth, chaosBarRect.Height);
        using var chaosPen = new Pen(Color.FromArgb(148, 160, 185));
        g.DrawRectangle(chaosPen, chaosBarRect.X, chaosBarRect.Y, chaosBarRect.Width, chaosBarRect.Height);
        g.DrawString($"Хаос  {_chaosLevel:0}/100", textFont, text, statsCard.X + 12, statsCard.Y + 128);

        var riskText = _chaosLevel < 40f ? "Ситуация спокойная" : _chaosLevel < 70f ? "Риск средний" : "Высокий риск";
        g.DrawString(riskText, textFont, subText, statsCard.X + 12, statsCard.Y + 154);
    }

    private void DrawOverlay(Graphics g)
    {
        using var overlay = new SolidBrush(Color.FromArgb(170, 12, 13, 18));
        g.FillRectangle(overlay, ClientRectangle);

        var title = _isGameOver
            ? (_isVictory ? "Смена завершена успешно" : "Смена завершена")
            : "Пауза";
        var subtitle = _isGameOver
            ? "Нажмите R чтобы начать заново или Esc/Q чтобы выйти"
            : "Нажмите Esc чтобы продолжить или R чтобы начать заново";

        using var cardBrush = new SolidBrush(Color.FromArgb(210, 28, 32, 42));
        using var cardBorder = new Pen(Color.FromArgb(130, 150, 172), 1);
        var box = new RectangleF(ClientRectangle.Width / 2f - 245, ClientRectangle.Height / 2f - 78, 490, 156);
        g.FillRectangle(cardBrush, box);
        g.DrawRectangle(cardBorder, box.X, box.Y, box.Width, box.Height);

        using var bigFont = new Font("Segoe UI", 16, FontStyle.Bold);
        using var subFont = new Font("Segoe UI", 10, FontStyle.Regular);
        var titleSize = g.MeasureString(title, bigFont);
        var subSize = g.MeasureString(subtitle, subFont);
        var cx = ClientRectangle.Width / 2f;
        var cy = ClientRectangle.Height / 2f;
        g.DrawString(title, bigFont, Brushes.WhiteSmoke, cx - titleSize.Width / 2f, cy - 40);
        g.DrawString(subtitle, subFont, Brushes.Gainsboro, cx - subSize.Width / 2f, cy - 2);
    }

    private void Form1_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.FromArgb(24, 26, 34));

        using var fieldBrush = new SolidBrush(Color.FromArgb(46, 52, 64));
        using var fieldBorderPen = new Pen(Color.FromArgb(125, 136, 156), 2);
        g.FillRectangle(fieldBrush, _playField);
        g.DrawRectangle(fieldBorderPen, _playField);
        DrawDormBuildings(g);

        if (_currentTask != null)
        {
            DrawTaskMarker(g, _currentTask);
        }

        DrawPlayer(g);
        DrawHud(g);

        if (_isPaused || _isGameOver)
        {
            DrawOverlay(g);
        }
    }
}
