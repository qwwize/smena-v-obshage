namespace SmenaVObshage.Game;

public partial class Form1 : Form
{
    private readonly System.Windows.Forms.Timer _gameTimer = new();
    private readonly HashSet<Keys> _pressedKeys = [];

    private readonly Rectangle _playField = new(20, 20, 760, 380);
    private readonly Size _playerSize = new(24, 24);
    private PointF _playerPosition = new(60, 60);
    private const float PlayerSpeedPerTick = 4f;

    public Form1()
    {
        InitializeComponent();
        Text = "Смена в общаге - MVP";
        DoubleBuffered = true;
        KeyPreview = true;

        _gameTimer.Interval = 16; // ~60 FPS
        _gameTimer.Tick += GameTimer_Tick;

        Paint += Form1_Paint;
        KeyDown += Form1_KeyDown;
        KeyUp += Form1_KeyUp;
        Shown += (_, _) => _gameTimer.Start();
    }

    private void GameTimer_Tick(object? sender, EventArgs e)
    {
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
        Invalidate();
    }

    private void ClampPlayerToField()
    {
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

    private void Form1_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.FromArgb(30, 30, 34));

        using var fieldBrush = new SolidBrush(Color.FromArgb(50, 52, 58));
        using var fieldBorderPen = new Pen(Color.FromArgb(120, 125, 140), 2);
        g.FillRectangle(fieldBrush, _playField);
        g.DrawRectangle(fieldBorderPen, _playField);

        var playerRect = new RectangleF(_playerPosition, _playerSize);
        using var playerBrush = new SolidBrush(Color.FromArgb(85, 195, 255));
        g.FillEllipse(playerBrush, playerRect);

        using var hudBrush = new SolidBrush(Color.WhiteSmoke);
        using var hudFont = new Font("Segoe UI", 10, FontStyle.Regular);
        g.DrawString("Движение: WASD / Стрелки", hudFont, hudBrush, 20, 410);
        g.DrawString($"Tick: {_gameTimer.Interval} ms", hudFont, hudBrush, 260, 410);
    }
}
