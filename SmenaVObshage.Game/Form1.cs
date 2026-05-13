using SmenaVObshage.Game.Controllers;
using SmenaVObshage.Game.Models;
using SmenaVObshage.Game.Views;

namespace SmenaVObshage.Game;

public partial class Form1 : Form
{
    private readonly System.Windows.Forms.Timer _gameTimer = new();
    private readonly GameState _state = new();
    private readonly GameController _controller;
    private readonly GameRenderer _renderer = new();

    public Form1()
    {
        InitializeComponent();
        Text = "Смена в общаге - MVP";
        DoubleBuffered = true;
        KeyPreview = true;

        _controller = new GameController(_state);

        _gameTimer.Interval = 16;
        _gameTimer.Tick += (_, _) =>
        {
            _controller.Tick(_gameTimer.Interval);
            Invalidate();
        };

        Paint += (_, e) => _renderer.Draw(e.Graphics, ClientRectangle, _state, _controller);
        KeyDown += (_, e) =>
        {
            if (_controller.HandleKeyDown(e.KeyCode))
            {
                Close();
                return;
            }

            Invalidate();
        };
        KeyUp += (_, e) => _controller.HandleKeyUp(e.KeyCode);
        Deactivate += (_, _) => _controller.HandleDeactivate();
        Shown += (_, _) =>
        {
            _controller.ResetGame();
            _gameTimer.Start();
        };
    }
}
