namespace SmenaVObshage.Game;

public enum GameEventKind
{
    /// <summary>Протечка — удерживать E ~2 с в зоне.</summary>
    Leak,
    /// <summary>Выбило пробки — один раз E в зоне.</summary>
    Breakers,
    /// <summary>Доставка уведомления — дойти до комнаты.</summary>
    Delivery,
    /// <summary>Шумные соседи — E в комнате.</summary>
    NoisyNeighbors,
    /// <summary>Срочная проверка коменданта — E в зоне до истечения времени.</summary>
    CommandantCheck,
}

public sealed class GameTask
{
    public Guid Id { get; } = Guid.NewGuid();
    public required GameEventKind Kind { get; init; }
    public required RectangleF Zone { get; init; }
    public bool IsUrgent { get; init; }
    /// <summary>Лимит в секундах; 0 — без лимита.</summary>
    public float TimeLimitSeconds { get; init; }
    public float HoldProgressMs { get; set; }
}
