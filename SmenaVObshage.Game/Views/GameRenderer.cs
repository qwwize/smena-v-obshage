using SmenaVObshage.Game.Controllers;
using SmenaVObshage.Game.Models;

namespace SmenaVObshage.Game.Views;

public sealed class GameRenderer
{
    public void Draw(Graphics g, Rectangle clientRectangle, GameState s, GameController controller)
    {
        g.Clear(Color.FromArgb(24, 26, 34));

        using var fieldBrush = new SolidBrush(Color.FromArgb(46, 52, 64));
        using var fieldBorderPen = new Pen(Color.FromArgb(125, 136, 156), 2);
        g.FillRectangle(fieldBrush, s.PlayField);
        g.DrawRectangle(fieldBorderPen, s.PlayField);

        DrawDormBuildings(g, s);

        if (s.CurrentTask != null)
        {
            DrawTaskMarker(g, s.CurrentTask);
        }

        DrawPlayer(g, s);
        DrawHud(g, s, controller);

        if (s.IsPaused || s.IsGameOver)
        {
            DrawOverlay(g, clientRectangle, s);
        }
    }

    private static void DrawDormBuildings(Graphics g, GameState s)
    {
        using var wallBrush = new SolidBrush(Color.FromArgb(78, 94, 118));
        using var wallPen = new Pen(Color.FromArgb(140, 168, 196), 1);
        using var blockBrush = new SolidBrush(Color.FromArgb(70, 76, 92));
        using var blockPen = new Pen(Color.FromArgb(136, 150, 176), 2);
        using var dotBrush = new SolidBrush(Color.FromArgb(150, 180, 120));

        foreach (var wall in s.WallSegments)
        {
            g.FillRectangle(wallBrush, wall);
            g.DrawRectangle(wallPen, wall.X, wall.Y, wall.Width, wall.Height);
        }

        foreach (var block in s.SolidBlocks)
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

    private static void DrawPlayer(Graphics g, GameState s)
    {
        var playerRect = new RectangleF(s.PlayerPosition, s.PlayerSize);
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

    private static void DrawTaskMarker(Graphics g, GameTask task)
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

    private static void DrawHud(Graphics g, GameState s, GameController controller)
    {
        var hudTop = s.PlayField.Bottom + 16;
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
        if (!string.IsNullOrEmpty(s.StatusMessage))
        {
            g.DrawString("Статус", titleFont, text, controlsCard.X + 12, controlsCard.Y + 154);
            g.DrawString(s.StatusMessage, textFont, Brushes.Khaki, controlsCard.X + 12, controlsCard.Y + 178);
        }

        g.DrawString("Задачи", titleFont, text, tasksCard.X + 12, tasksCard.Y + 10);
        var rowY = tasksCard.Y + 38;
        if (s.CurrentTask != null)
        {
            var cur = s.CurrentTask;
            var currentLine = $"Сейчас  {KindTitle(cur.Kind)}";
            if (cur.TimeLimitSeconds > 0f)
            {
                var start = s.TaskStartUtc.TryGetValue(cur.Id, out var t) ? t : DateTime.UtcNow;
                var left = cur.TimeLimitSeconds - (float)(DateTime.UtcNow - start).TotalSeconds;
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
        var show = Math.Min(4, s.TaskQueue.Count);
        if (show == 0)
        {
            g.DrawString("Пусто", textFont, subText, tasksCard.X + 12, rowY);
        }
        else
        {
            for (var i = 0; i < show; i++)
            {
                var task = s.TaskQueue[i];
                var label = $"{i + 1}. {(task.IsUrgent ? "[СРОЧНО] " : string.Empty)}{KindTitle(task.Kind)}";
                g.DrawString(label, textFont, task.IsUrgent ? Brushes.OrangeRed : text, tasksCard.X + 12, rowY);
                rowY += 20;
            }
        }

        g.DrawString("Показатели смены", titleFont, text, statsCard.X + 12, statsCard.Y + 10);
        var timeLeft = TimeSpan.FromSeconds(MathF.Max(0f, s.SessionTimeLeftSeconds));
        g.DrawString($"Время  {timeLeft.Minutes:00}:{timeLeft.Seconds:00}", titleFont, text, statsCard.X + 12, statsCard.Y + 40);
        g.DrawString($"Счет  {s.CompletedCount}/{controller.TargetCompleted}", textFont, text, statsCard.X + 12, statsCard.Y + 66);
        g.DrawString($"Сложность  {controller.DifficultyStage + 1}", textFont, text, statsCard.X + 12, statsCard.Y + 84);

        var chaosBarRect = new RectangleF(statsCard.X + 12, statsCard.Y + 112, statsCard.Width - 24, 18);
        using var chaosBack = new SolidBrush(Color.FromArgb(66, 74, 92));
        g.FillRectangle(chaosBack, chaosBarRect);
        var chaosColor = s.ChaosLevel < 40f
            ? Color.FromArgb(81, 183, 126)
            : s.ChaosLevel < 70f ? Color.FromArgb(228, 176, 74) : Color.FromArgb(224, 85, 85);
        using var chaosFill = new SolidBrush(chaosColor);
        var fillWidth = chaosBarRect.Width * (s.ChaosLevel / 100f);
        g.FillRectangle(chaosFill, chaosBarRect.X, chaosBarRect.Y, fillWidth, chaosBarRect.Height);
        using var chaosPen = new Pen(Color.FromArgb(148, 160, 185));
        g.DrawRectangle(chaosPen, chaosBarRect.X, chaosBarRect.Y, chaosBarRect.Width, chaosBarRect.Height);
        g.DrawString($"Хаос  {s.ChaosLevel:0}/100", textFont, text, statsCard.X + 12, statsCard.Y + 138);

        var riskText = s.ChaosLevel < 40f ? "Ситуация спокойная" : s.ChaosLevel < 70f ? "Риск средний" : "Высокий риск";
        g.DrawString(riskText, textFont, subText, statsCard.X + 12, statsCard.Y + 162);
        if (s.HighChaosSeconds > 0f)
        {
            g.DrawString($"До поражения по хаосу  {controller.ChaosDefeatTimeLeft:0.0}с", textFont, Brushes.OrangeRed, statsCard.X + 12, statsCard.Y + 184);
        }
    }

    private static void DrawOverlay(Graphics g, Rectangle clientRectangle, GameState s)
    {
        using var overlay = new SolidBrush(Color.FromArgb(170, 12, 13, 18));
        g.FillRectangle(overlay, clientRectangle);

        var title = s.IsGameOver
            ? (s.IsVictory ? "Смена завершена успешно" : "Смена завершена")
            : "Пауза";
        var subtitle = s.IsGameOver
            ? "Нажмите R чтобы начать заново или Esc/Q чтобы выйти"
            : "Нажмите Esc чтобы продолжить или R чтобы начать заново";

        using var cardBrush = new SolidBrush(Color.FromArgb(210, 28, 32, 42));
        using var cardBorder = new Pen(Color.FromArgb(130, 150, 172), 1);
        var box = new RectangleF(clientRectangle.Width / 2f - 245, clientRectangle.Height / 2f - 78, 490, 156);
        g.FillRectangle(cardBrush, box);
        g.DrawRectangle(cardBorder, box.X, box.Y, box.Width, box.Height);

        using var bigFont = new Font("Segoe UI", 16, FontStyle.Bold);
        using var subFont = new Font("Segoe UI", 10, FontStyle.Regular);
        var titleSize = g.MeasureString(title, bigFont);
        var subSize = g.MeasureString(subtitle, subFont);
        var cx = clientRectangle.Width / 2f;
        var cy = clientRectangle.Height / 2f;
        g.DrawString(title, bigFont, Brushes.WhiteSmoke, cx - titleSize.Width / 2f, cy - 40);
        g.DrawString(subtitle, subFont, Brushes.Gainsboro, cx - subSize.Width / 2f, cy - 2);
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
}
