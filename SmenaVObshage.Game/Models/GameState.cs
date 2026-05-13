namespace SmenaVObshage.Game.Models;

public sealed class GameState
{
    public Rectangle PlayField { get; } = new(20, 20, 940, 420);
    public Size PlayerSize { get; } = new(24, 24);
    public PointF PlayerPosition { get; set; } = new(72, 72);

    public HashSet<Keys> PressedKeys { get; } = [];
    public List<GameTask> TaskQueue { get; } = [];
    public Random Random { get; } = new();
    public List<RectangleF> WallSegments { get; } = [];
    public List<RectangleF> SolidBlocks { get; } = [];
    public Dictionary<Guid, DateTime> TaskStartUtc { get; } = [];

    public GameTask? CurrentTask { get; set; }
    public int SpawnCooldownTicks { get; set; }
    public bool EWasDown { get; set; }
    public int CompletedCount { get; set; }
    public float SessionTimeLeftSeconds { get; set; } = 180f;
    public float ElapsedSessionSeconds { get; set; }
    public float ChaosLevel { get; set; }
    public float HighChaosSeconds { get; set; }
    public bool IsPaused { get; set; }
    public bool IsGameOver { get; set; }
    public bool IsVictory { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public int StatusTicksRemaining { get; set; }
}
