using SmenaVObshage.Game.Models;

namespace SmenaVObshage.Game.Controllers;

public sealed class GameController(GameState state)
{
    private const float LeakHoldMs = 2000f;
    private const int SpawnIntervalTicks = 240;
    private const int MaxActiveAndQueued = 6;
    private const float SessionDurationSeconds = 180f;
    private const int TargetCompletedTasks = 14;
    private const float ChaosPassivePerSecond = 1.2f;
    private const float ChaosPenaltyOnFail = 14f;
    private const float ChaosReduceOnComplete = 7f;
    private const float DifficultyStepSeconds = 60f;
    private const int MinSpawnIntervalTicks = 120;
    private const float HighChaosThreshold = 80f;
    private const float HighChaosDefeatSeconds = 20f;
    private const float PlayerSpeedPerTick = 4f;

    private readonly GameState _s = state;

    public int TargetCompleted => TargetCompletedTasks;
    public int DifficultyStage => (int)(_s.ElapsedSessionSeconds / DifficultyStepSeconds);
    public float ChaosDefeatTimeLeft => Math.Max(0f, HighChaosDefeatSeconds - _s.HighChaosSeconds);

    public void ResetGame()
    {
        _s.PlayerPosition = new PointF(72, 72);
        _s.PressedKeys.Clear();
        _s.TaskQueue.Clear();
        _s.WallSegments.Clear();
        _s.SolidBlocks.Clear();
        _s.TaskStartUtc.Clear();
        _s.CurrentTask = null;
        _s.SpawnCooldownTicks = 0;
        _s.EWasDown = false;
        _s.CompletedCount = 0;
        _s.ChaosLevel = 0f;
        _s.ElapsedSessionSeconds = 0f;
        _s.HighChaosSeconds = 0f;
        _s.SessionTimeLeftSeconds = SessionDurationSeconds;
        _s.IsPaused = false;
        _s.IsGameOver = false;
        _s.IsVictory = false;
        _s.StatusMessage = "Новая смена началась";
        _s.StatusTicksRemaining = 100;
        SeedInitialTasks();
    }

    public void HandleDeactivate()
    {
        _s.PressedKeys.Clear();
        _s.EWasDown = false;
    }

    public bool HandleKeyDown(Keys keyCode)
    {
        if (keyCode == Keys.Escape)
        {
            if (_s.IsGameOver)
            {
                return true;
            }

            _s.IsPaused = !_s.IsPaused;
            _s.StatusMessage = _s.IsPaused ? "Пауза" : "Продолжение смены";
            _s.StatusTicksRemaining = 80;
            return false;
        }

        if (keyCode == Keys.R)
        {
            ResetGame();
            return false;
        }

        if (keyCode == Keys.Q)
        {
            return true;
        }

        _s.PressedKeys.Add(keyCode);
        return false;
    }

    public void HandleKeyUp(Keys keyCode) => _s.PressedKeys.Remove(keyCode);

    public void Tick(int intervalMs)
    {
        if (_s.IsPaused || _s.IsGameOver)
        {
            return;
        }

        var dtSeconds = intervalMs / 1000f;
        _s.ElapsedSessionSeconds = Math.Min(SessionDurationSeconds, _s.ElapsedSessionSeconds + dtSeconds);
        _s.SessionTimeLeftSeconds = Math.Max(0f, _s.SessionTimeLeftSeconds - dtSeconds);
        _s.ChaosLevel = Math.Clamp(_s.ChaosLevel + ChaosPassivePerSecond * dtSeconds, 0f, 100f);

        if (_s.ChaosLevel >= HighChaosThreshold)
        {
            _s.HighChaosSeconds += dtSeconds;
            if (_s.HighChaosSeconds >= HighChaosDefeatSeconds)
            {
                FinishByChaos();
                return;
            }
        }
        else
        {
            _s.HighChaosSeconds = 0f;
        }

        if (_s.SessionTimeLeftSeconds <= 0f)
        {
            FinishByTime();
            return;
        }

        var dx = 0f;
        var dy = 0f;

        if (_s.PressedKeys.Contains(Keys.A) || _s.PressedKeys.Contains(Keys.Left))
        {
            dx -= PlayerSpeedPerTick;
        }

        if (_s.PressedKeys.Contains(Keys.D) || _s.PressedKeys.Contains(Keys.Right))
        {
            dx += PlayerSpeedPerTick;
        }

        if (_s.PressedKeys.Contains(Keys.W) || _s.PressedKeys.Contains(Keys.Up))
        {
            dy -= PlayerSpeedPerTick;
        }

        if (_s.PressedKeys.Contains(Keys.S) || _s.PressedKeys.Contains(Keys.Down))
        {
            dy += PlayerSpeedPerTick;
        }

        MovePlayerWithCollision(dx, dy);

        if (_s.StatusTicksRemaining > 0)
        {
            _s.StatusTicksRemaining--;
            if (_s.StatusTicksRemaining == 0)
            {
                _s.StatusMessage = string.Empty;
            }
        }

        var eNow = _s.PressedKeys.Contains(Keys.E);
        var ePressedThisFrame = eNow && !_s.EWasDown;
        _s.EWasDown = eNow;

        EnsureCurrentTask();
        TrySpawnTask();
        UpdateCurrentTask(eNow, ePressedThisFrame, intervalMs);
    }

    private void SeedInitialTasks()
    {
        EnqueueTask(CreateTask(GameEventKind.Delivery));
        EnqueueTask(CreateTask(GameEventKind.Breakers));
        EnsureCurrentTask();
    }

    private void EnsureCurrentTask()
    {
        if (_s.CurrentTask != null || _s.TaskQueue.Count == 0)
        {
            return;
        }

        var nextTask = _s.TaskQueue[0];
        _s.TaskQueue.RemoveAt(0);
        _s.CurrentTask = BuildReachableCurrentTask(nextTask);
    }

    private void TrySpawnTask()
    {
        var total = (_s.CurrentTask != null ? 1 : 0) + _s.TaskQueue.Count;
        if (total >= MaxActiveAndQueued)
        {
            return;
        }

        if (++_s.SpawnCooldownTicks < GetCurrentSpawnIntervalTicks())
        {
            return;
        }

        _s.SpawnCooldownTicks = 0;
        EnqueueTask(CreateTask(PickRandomKind()));
        EnsureCurrentTask();
    }

    private void UpdateCurrentTask(bool eHeld, bool ePressedThisFrame, int intervalMs)
    {
        var task = _s.CurrentTask;
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
                    task.HoldProgressMs += intervalMs;
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
        if (!_s.TaskStartUtc.TryGetValue(task.Id, out var t))
        {
            t = DateTime.UtcNow;
            _s.TaskStartUtc[task.Id] = t;
        }
        return t;
    }

    private void CompleteCurrentTask()
    {
        if (_s.CurrentTask != null)
        {
            _s.TaskStartUtc.Remove(_s.CurrentTask.Id);
        }

        _s.CompletedCount++;
        _s.ChaosLevel = Math.Clamp(_s.ChaosLevel - ChaosReduceOnComplete, 0f, 100f);
        _s.StatusMessage = "Задача выполнена";
        _s.StatusTicksRemaining = 115;
        _s.CurrentTask = null;
        EnsureCurrentTask();
    }

    private void FailCurrentTask(string message)
    {
        if (_s.CurrentTask != null)
        {
            _s.TaskStartUtc.Remove(_s.CurrentTask.Id);
        }

        _s.ChaosLevel = Math.Clamp(_s.ChaosLevel + ChaosPenaltyOnFail, 0f, 100f);
        _s.StatusMessage = message;
        _s.StatusTicksRemaining = 160;
        _s.CurrentTask = null;
        EnsureCurrentTask();
    }

    private void FinishByTime()
    {
        _s.IsGameOver = true;
        _s.IsVictory = _s.CompletedCount >= TargetCompletedTasks && _s.ChaosLevel < 80f;
        _s.StatusMessage = _s.IsVictory ? "Смена успешно завершена" : "Смена окончена неудачей";
        _s.StatusTicksRemaining = 0;
    }

    private void FinishByChaos()
    {
        _s.IsGameOver = true;
        _s.IsVictory = false;
        _s.StatusMessage = "Поражение хаос вышел из-под контроля";
        _s.StatusTicksRemaining = 0;
    }

    private bool PlayerInZone(RectangleF zone)
    {
        var cx = _s.PlayerPosition.X + _s.PlayerSize.Width / 2f;
        var cy = _s.PlayerPosition.Y + _s.PlayerSize.Height / 2f;
        return zone.Contains(cx, cy);
    }

    private void EnqueueTask(GameTask task)
    {
        if (!task.IsUrgent)
        {
            _s.TaskQueue.Add(task);
            return;
        }

        var i = 0;
        while (i < _s.TaskQueue.Count && _s.TaskQueue[i].IsUrgent)
        {
            i++;
        }
        _s.TaskQueue.Insert(i, task);
    }

    private GameEventKind PickRandomKind()
    {
        if (_s.Random.Next(100) < GetUrgentChancePercent())
        {
            return GameEventKind.CommandantCheck;
        }

        GameEventKind[] normal =
        [
            GameEventKind.Leak, GameEventKind.Breakers, GameEventKind.Delivery, GameEventKind.NoisyNeighbors,
        ];
        return normal[_s.Random.Next(normal.Length)];
    }

    private int GetCurrentSpawnIntervalTicks()
    {
        var interval = SpawnIntervalTicks - DifficultyStage * 24;
        return Math.Max(MinSpawnIntervalTicks, interval);
    }

    private int GetUrgentChancePercent() => Math.Min(40, 14 + DifficultyStage * 6);

    private GameTask CreateTask(GameEventKind kind)
    {
        return kind switch
        {
            GameEventKind.Leak => new GameTask { Kind = kind, Zone = RandomPointZone(44f), IsUrgent = false, TimeLimitSeconds = 0f },
            GameEventKind.Breakers => new GameTask { Kind = kind, Zone = RandomPointZone(40f), IsUrgent = false, TimeLimitSeconds = 0f },
            GameEventKind.Delivery => new GameTask { Kind = kind, Zone = RandomRoomZone(118f, 88f), IsUrgent = false, TimeLimitSeconds = 0f },
            GameEventKind.NoisyNeighbors => new GameTask { Kind = kind, Zone = RandomRoomZone(102f, 78f), IsUrgent = false, TimeLimitSeconds = 0f },
            GameEventKind.CommandantCheck => new GameTask { Kind = kind, Zone = RandomPointZone(38f), IsUrgent = true, TimeLimitSeconds = 18f },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private RectangleF RandomRoomZone(float width, float height)
    {
        var margin = 10f;
        var minX = (int)MathF.Ceiling(_s.PlayField.Left + margin);
        var minY = (int)MathF.Ceiling(_s.PlayField.Top + margin);
        var maxX = (int)MathF.Floor(_s.PlayField.Right - width - margin);
        var maxY = (int)MathF.Floor(_s.PlayField.Bottom - height - margin);
        if (maxX <= minX) maxX = minX + 1;
        if (maxY <= minY) maxY = minY + 1;

        for (var i = 0; i < 120; i++)
        {
            var x = _s.Random.Next(minX, maxX);
            var y = _s.Random.Next(minY, maxY);
            var zone = new RectangleF(x, y, width, height);
            if (IsZoneWalkable(zone))
            {
                return zone;
            }
        }

        return new RectangleF(_s.PlayField.Left + 20, _s.PlayField.Top + 20, width, height);
    }

    private RectangleF RandomPointZone(float size)
    {
        var half = size / 2f;
        var minCx = (int)MathF.Ceiling(_s.PlayField.Left + half + 8f);
        var minCy = (int)MathF.Ceiling(_s.PlayField.Top + half + 8f);
        var maxCx = (int)MathF.Floor(_s.PlayField.Right - half - 8f);
        var maxCy = (int)MathF.Floor(_s.PlayField.Bottom - half - 8f);
        if (maxCx <= minCx) maxCx = minCx + 1;
        if (maxCy <= minCy) maxCy = minCy + 1;

        for (var i = 0; i < 120; i++)
        {
            var cx = _s.Random.Next(minCx, maxCx);
            var cy = _s.Random.Next(minCy, maxCy);
            var zone = new RectangleF(cx - half, cy - half, size, size);
            if (IsZoneWalkable(zone))
            {
                return zone;
            }
        }
        return new RectangleF(_s.PlayField.Left + 30, _s.PlayField.Top + 30, size, size);
    }

    private void MovePlayerWithCollision(float dx, float dy)
    {
        var xCandidate = new PointF(_s.PlayerPosition.X + dx, _s.PlayerPosition.Y);
        ClampPointToField(ref xCandidate);
        if (!IntersectsObstacles(GetPlayerRect(xCandidate)))
        {
            _s.PlayerPosition = xCandidate;
        }

        var yCandidate = new PointF(_s.PlayerPosition.X, _s.PlayerPosition.Y + dy);
        ClampPointToField(ref yCandidate);
        if (!IntersectsObstacles(GetPlayerRect(yCandidate)))
        {
            _s.PlayerPosition = yCandidate;
        }
    }

    private void ClampPointToField(ref PointF p)
    {
        p.X = Math.Clamp(p.X, _s.PlayField.Left, _s.PlayField.Right - _s.PlayerSize.Width);
        p.Y = Math.Clamp(p.Y, _s.PlayField.Top, _s.PlayField.Bottom - _s.PlayerSize.Height);
    }

    private RectangleF GetPlayerRect(PointF p) => new(p.X, p.Y, _s.PlayerSize.Width, _s.PlayerSize.Height);

    private bool IntersectsObstacles(RectangleF rect) =>
        _s.WallSegments.Any(rect.IntersectsWith) || _s.SolidBlocks.Any(rect.IntersectsWith);

    private bool IsZoneWalkable(RectangleF zone) =>
        !_s.WallSegments.Any(zone.IntersectsWith) && !_s.SolidBlocks.Any(zone.IntersectsWith);

    private void GenerateObstacleLayout()
    {
        _s.WallSegments.Clear();
        _s.SolidBlocks.Clear();

        var t = 10f;
        var minX = _s.PlayField.Left + 26;
        var maxX = _s.PlayField.Right - 26;
        var minY = _s.PlayField.Top + 26;
        var maxY = _s.PlayField.Bottom - 26;

        AddHorizontalWall(minY + 88, minX, maxX, t);
        AddHorizontalWall(minY + 188, minX, maxX, t);
        AddVerticalWall(minX + 220, minY, maxY, t);
        AddVerticalWall(minX + 480, minY, maxY, t);

        for (var i = 0; i < 5; i++)
        {
            var w = _s.Random.Next(56, 86);
            var h = _s.Random.Next(42, 72);
            var x = _s.Random.Next((int)minX + 20, (int)maxX - w - 20);
            var y = _s.Random.Next((int)minY + 20, (int)maxY - h - 20);
            var candidate = new RectangleF(x, y, w, h);
            if (!IntersectsAny(candidate) && !TooCloseToWalls(candidate, 16f))
            {
                _s.SolidBlocks.Add(candidate);
            }
        }

        EnsurePlayerOutsideObstacles();
    }

    private void AddHorizontalWall(float y, float x1, float x2, float t)
    {
        var gapW = _s.Random.Next(95, 145);
        var gapX = _s.Random.Next((int)x1 + 60, (int)x2 - gapW - 60);
        var left = new RectangleF(x1, y, gapX - x1, t);
        var right = new RectangleF(gapX + gapW, y, x2 - (gapX + gapW), t);
        if (left.Width > 20) _s.WallSegments.Add(left);
        if (right.Width > 20) _s.WallSegments.Add(right);
    }

    private void AddVerticalWall(float x, float y1, float y2, float t)
    {
        var gapH = _s.Random.Next(82, 132);
        var gapY = _s.Random.Next((int)y1 + 50, (int)y2 - gapH - 50);
        var top = new RectangleF(x, y1, t, gapY - y1);
        var bottom = new RectangleF(x, gapY + gapH, t, y2 - (gapY + gapH));
        if (top.Height > 20) _s.WallSegments.Add(top);
        if (bottom.Height > 20) _s.WallSegments.Add(bottom);
    }

    private bool IntersectsAny(RectangleF rect) => _s.WallSegments.Any(rect.IntersectsWith) || _s.SolidBlocks.Any(rect.IntersectsWith);

    private bool TooCloseToWalls(RectangleF rect, float margin)
    {
        var inflated = RectangleF.Inflate(rect, margin, margin);
        return _s.WallSegments.Any(inflated.IntersectsWith);
    }

    private void EnsurePlayerOutsideObstacles()
    {
        if (!IntersectsObstacles(GetPlayerRect(_s.PlayerPosition)))
        {
            return;
        }

        for (var i = 0; i < 120; i++)
        {
            var x = _s.Random.Next(_s.PlayField.Left + 12, _s.PlayField.Right - _s.PlayerSize.Width - 12);
            var y = _s.Random.Next(_s.PlayField.Top + 12, _s.PlayField.Bottom - _s.PlayerSize.Height - 12);
            var candidate = new PointF(x, y);
            if (!IntersectsObstacles(GetPlayerRect(candidate)))
            {
                _s.PlayerPosition = candidate;
                return;
            }
        }

        _s.PlayerPosition = new PointF(_s.PlayField.Left + 14, _s.PlayField.Top + 14);
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

    private GameTask BuildReachableCurrentTask(GameTask sourceTask)
    {
        for (var i = 0; i < 50; i++)
        {
            GenerateObstacleLayout();
            var candidate = RebuildTaskForCurrentLayout(sourceTask);
            if (HasWalkablePath(GetPlayerCenter(_s.PlayerPosition), GetZoneCenter(candidate.Zone)))
            {
                return candidate;
            }
        }

        return RebuildTaskForCurrentLayout(sourceTask);
    }

    private PointF GetPlayerCenter(PointF p) => new(p.X + _s.PlayerSize.Width / 2f, p.Y + _s.PlayerSize.Height / 2f);
    private static PointF GetZoneCenter(RectangleF zone) => new(zone.X + zone.Width / 2f, zone.Y + zone.Height / 2f);

    private bool HasWalkablePath(PointF from, PointF to)
    {
        const int cell = 20;
        var cols = (int)(_s.PlayField.Width / cell);
        var rows = (int)(_s.PlayField.Height / cell);
        if (cols <= 0 || rows <= 0)
        {
            return false;
        }

        var start = ToCell(from, cell, cols, rows);
        var goal = ToCell(to, cell, cols, rows);
        if (start == goal)
        {
            return true;
        }

        if (!IsCellWalkable(start.x, start.y, cell) || !IsCellWalkable(goal.x, goal.y, cell))
        {
            return false;
        }

        var visited = new bool[cols, rows];
        var q = new Queue<(int x, int y)>();
        visited[start.x, start.y] = true;
        q.Enqueue(start);
        var dirs = new (int x, int y)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

        while (q.Count > 0)
        {
            var (cx, cy) = q.Dequeue();
            foreach (var (dx, dy) in dirs)
            {
                var nx = cx + dx;
                var ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= cols || ny >= rows || visited[nx, ny])
                {
                    continue;
                }

                if (!IsCellWalkable(nx, ny, cell))
                {
                    continue;
                }

                if (nx == goal.x && ny == goal.y)
                {
                    return true;
                }

                visited[nx, ny] = true;
                q.Enqueue((nx, ny));
            }
        }

        return false;
    }

    private (int x, int y) ToCell(PointF point, int cell, int cols, int rows)
    {
        var localX = point.X - _s.PlayField.Left;
        var localY = point.Y - _s.PlayField.Top;
        var x = Math.Clamp((int)(localX / cell), 0, cols - 1);
        var y = Math.Clamp((int)(localY / cell), 0, rows - 1);
        return (x, y);
    }

    private bool IsCellWalkable(int x, int y, int cell)
    {
        var rect = new RectangleF(_s.PlayField.Left + x * cell + 2, _s.PlayField.Top + y * cell + 2, cell - 4, cell - 4);
        return !IntersectsObstacles(rect);
    }
}
