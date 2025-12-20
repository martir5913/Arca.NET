namespace Arca.NET.Services;

/// <summary>
/// Servicio para manejar el icono en la bandeja del sistema (System Tray).
/// Permite que la aplicación corra en segundo plano.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private bool _disposed;

    public event EventHandler? ShowWindowRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? LockVaultRequested;

    public TrayIconService()
    {
        // Crear menú contextual
        _contextMenu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Abrir Arca", null, OnOpenClick);
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
        _contextMenu.Items.Add(openItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var lockItem = new ToolStripMenuItem("Bloquear Vault", null, OnLockClick);
        _contextMenu.Items.Add(lockItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Salir", null, OnExitClick);
        _contextMenu.Items.Add(exitItem);

        // Crear icono en la bandeja
        _notifyIcon = new NotifyIcon
        {
            Icon = CreateDefaultIcon(),
            Text = "Arca - Vault Manager",
            ContextMenuStrip = _contextMenu,
            Visible = false
        };

        _notifyIcon.DoubleClick += OnNotifyIconDoubleClick;
    }

    public void Show()
    {
        _notifyIcon.Visible = true;
    }

    public void Hide()
    {
        _notifyIcon.Visible = false;
    }

    public void UpdateStatus(bool isUnlocked, int secretCount)
    {
        var status = isUnlocked ? "Desbloqueado" : "Bloqueado";
        _notifyIcon.Text = $"Arca - {status}\n{secretCount} secreto(s)";

        // Actualizar icono del menú de bloqueo
        if (_contextMenu.Items[2] is ToolStripMenuItem lockItem)
        {
            lockItem.Enabled = isUnlocked;
        }
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(3000, title, message, icon);
    }

    private void OnNotifyIconDoubleClick(object? sender, EventArgs e)
    {
        ShowWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpenClick(object? sender, EventArgs e)
    {
        ShowWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnLockClick(object? sender, EventArgs e)
    {
        LockVaultRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnExitClick(object? sender, EventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private static Icon CreateDefaultIcon()
    {
        // Intentar cargar icono desde recursos
        try
        {
            var iconPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets",
                "icon.ico");

            if (System.IO.File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
        }
        catch { }

        // Crear un icono simple si no existe
        return CreateSimpleIcon();
    }

    private static Icon CreateSimpleIcon()
    {
        // Crear un icono simple de 16x16 con un candado
        var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.FromArgb(233, 69, 96)); // Color #e94560

            // Dibujar un candado simple
            using var pen = new Pen(Color.White, 1);
            using var brush = new SolidBrush(Color.White);

            // Arco del candado
            g.DrawArc(pen, 4, 2, 7, 6, 180, 180);

            // Cuerpo del candado
            g.FillRectangle(brush, 3, 7, 10, 7);

            // Cerradura
            g.FillEllipse(new SolidBrush(Color.FromArgb(233, 69, 96)), 6, 9, 4, 3);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
    }
}
