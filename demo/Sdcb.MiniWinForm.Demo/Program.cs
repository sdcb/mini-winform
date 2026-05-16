using System.Drawing;
using Sdcb.MiniWinForm;
using Timer = Sdcb.MiniWinForm.Timer;

Application.SetHighDpiMode(HighDpiMode.SystemAware);
Application.EnableVisualStyles();
Application.Run(CreateMainForm());

static Form CreateMainForm()
{
    Form form = new()
    {
        Text = "Sdcb.MiniWinForm Demo",
        Width = 760,
        Height = 560,
    };

    MenuStrip menu = new();
    ToolStripMenuItem fileMenu = new("File");
    fileMenu.DropDownItems.Add(new ToolStripMenuItem("Show task dialog", (_, _) => ShowTaskDialog(form)));
    fileMenu.DropDownItems.Add(new ToolStripMenuItem("Exit", (_, _) => form.Close()));
    ToolStripMenuItem helpMenu = new("Help");
    helpMenu.DropDownItems.Add(new ToolStripMenuItem("About", (_, _) => ShowAbout(form)));
    menu.Items.AddRange(fileMenu, helpMenu);
    form.MainMenuStrip = menu;

    Label title = new()
    {
        Left = 20,
        Top = 44,
        Width = 680,
        Height = 24,
        Text = "A lightweight Win32 UI layer for tiny Native AOT tools.",
    };

    Label description = new()
    {
        Left = 20,
        Top = 76,
        Width = 680,
        Height = 52,
        Text = "This demo intentionally stays small. It shows layout, menus, progress, list selection, timers, and task dialogs using the WinForms-like API surface.",
    };

    TextBox input = new()
    {
        Left = 20,
        Top = 140,
        Width = 320,
        Text = "Native AOT utility",
    };

    CheckBox marquee = new()
    {
        Left = 360,
        Top = 138,
        Width = 220,
        Text = "Use marquee progress bar",
    };

    ListBox scenarios = new()
    {
        Left = 20,
        Top = 180,
        Width = 220,
        Height = 180,
    };
    scenarios.Items.AddRange("Launcher", "Image helper", "Config tool", "Batch runner");
    scenarios.SelectedIndex = 0;

    TextBox notes = new()
    {
        Left = 260,
        Top = 180,
        Width = 440,
        Height = 180,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        Text = "Pick a scenario on the left, then click Start demo to update the progress bar and status text.",
    };

    ProgressBar progress = new()
    {
        Left = 20,
        Top = 386,
        Width = 680,
        Height = 24,
    };

    Label status = new()
    {
        Left = 20,
        Top = 422,
        Width = 680,
        Height = 30,
        Text = "Ready.",
    };

    Button startButton = new()
    {
        Left = 20,
        Top = 464,
        Width = 130,
        Text = "Start demo",
        BackColor = Color.FromArgb(230, 240, 255),
    };

    Button resetButton = new()
    {
        Left = 164,
        Top = 464,
        Width = 130,
        Text = "Reset",
    };

    Timer timer = new()
    {
        Interval = 120,
    };

    startButton.Click += (_, _) =>
    {
        progress.Style = marquee.Checked ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
        progress.Value = 0;
        status.Text = $"Running {scenarios.SelectedItem ?? "demo"} for {input.Text}...";
        timer.Start();
    };

    resetButton.Click += (_, _) =>
    {
        timer.Stop();
        marquee.Checked = false;
        progress.Style = ProgressBarStyle.Blocks;
        progress.Value = 0;
        status.Text = "Ready.";
        notes.Text = "Pick a scenario on the left, then click Start demo to update the progress bar and status text.";
    };

    scenarios.SelectedIndexChanged += (_, _) =>
    {
        notes.Text = $"Scenario: {scenarios.SelectedItem}\r\nTarget: {input.Text}\r\n\r\nThis is the kind of small GUI helper MiniWinForm is designed for when shipping as a Native AOT executable.";
    };

    timer.Tick += (_, _) =>
    {
        if (progress.Style == ProgressBarStyle.Marquee)
        {
            status.Text = $"Marquee mode running for {input.Text}.";
            return;
        }

        progress.Increment(10);
        status.Text = $"Progress: {progress.Value}%";
        if (progress.Value >= progress.Maximum)
        {
            timer.Stop();
            status.Text = $"Finished {scenarios.SelectedItem ?? "demo"} for {input.Text}.";
        }
    };

    marquee.CheckedChanged += (_, _) =>
    {
        progress.Style = marquee.Checked ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
        if (!marquee.Checked)
        {
            progress.Value = 0;
        }
    };

    form.Controls.Add(menu);
    form.Controls.Add(title);
    form.Controls.Add(description);
    form.Controls.Add(input);
    form.Controls.Add(marquee);
    form.Controls.Add(scenarios);
    form.Controls.Add(notes);
    form.Controls.Add(progress);
    form.Controls.Add(status);
    form.Controls.Add(startButton);
    form.Controls.Add(resetButton);
    return form;
}

static void ShowTaskDialog(Form owner)
{
    TaskDialogPage page = new()
    {
        Caption = "MiniWinForm demo",
        Heading = "TaskDialog is also available",
        Text = "This is useful for tiny Native AOT desktop utilities that still need a proper GUI workflow.",
    };
    page.Buttons.AddRange(TaskDialogButton.OK, TaskDialogButton.Cancel);
    _ = TaskDialog.ShowDialog(owner, page);
}

static void ShowAbout(Form owner)
{
    TaskDialogPage page = new()
    {
        Caption = "About",
        Heading = "Sdcb.MiniWinForm",
        Text = "A small Win32 GUI toolkit with a WinForms-like API shape, intended for tiny Native AOT utilities.",
    };
    page.Buttons.Add(TaskDialogButton.OK);
    _ = TaskDialog.ShowDialog(owner, page);
}