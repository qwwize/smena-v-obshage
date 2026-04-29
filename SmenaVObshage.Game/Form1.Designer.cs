namespace SmenaVObshage.Game;

partial class Form1
{
    /// <summary>
    ///  Переменная контейнера компонентов
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Освобождение используемых ресурсов
    /// </summary>
    /// <param name="disposing">true если нужно освободить управляемые ресурсы иначе false</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Код созданный дизайнером Windows Forms

    /// <summary>
    ///  Метод для поддержки дизайнера
    ///  Не изменяйте содержимое вручную
    /// </summary>
    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(980, 690);
        this.Text = "Form1";
    }

    #endregion
}
