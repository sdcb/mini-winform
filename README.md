# Sdcb.MiniWinForm [![NuGet Downloads](https://img.shields.io/nuget/dt/Sdcb.MiniWinForm?style=flat-square&logo=nuget)](https://www.nuget.org/packages/Sdcb.MiniWinForm) [![License: MIT](https://img.shields.io/badge/license-MIT-green.svg?style=flat-square)](LICENSE)

Sdcb.MiniWinForm is a small Win32 GUI toolkit for .NET Native AOT utilities.

The goal is simple: keep a programming model that feels familiar to WinForms, but make it practical to publish very small GUI executables for Windows. It is a good fit for launchers, helper tools, config editors, and other tiny desktop utilities where a full WinForms deployment is heavier than necessary.

## Why this project exists

- Native AOT is great for tiny tools, but adding a GUI often increases size and complexity.
- WinForms has a productive API shape, and this project intentionally stays close to that style.
- Sdcb.MiniWinForm focuses on the subset that is most useful for small tools instead of trying to be a full WinForms replacement.

## Install

```bash
dotnet add package Sdcb.MiniWinForm
```

## Quick example

```csharp
using Sdcb.MiniWinForm;

Application.EnableVisualStyles();

Form form = new()
{
    Text = "Mini tool",
    Width = 360,
    Height = 180,
};

Button button = new()
{
    Left = 20,
    Top = 20,
    Width = 120,
    Text = "Show dialog",
};

button.Click += (_, _) =>
{
    TaskDialogPage page = new()
    {
        Caption = "MiniWinForm",
        Heading = "Native AOT friendly GUI",
        Text = "This is a tiny Win32 window with a WinForms-like API.",
    };
    page.Buttons.Add(TaskDialogButton.OK);
    _ = TaskDialog.ShowDialog(form, page);
};

form.Controls.Add(button);
Application.Run(form);
```

## Demo

The repository includes a small demo app in [demo/Sdcb.MiniWinForm.Demo](demo/Sdcb.MiniWinForm.Demo). It shows representative features such as:

- forms and controls
- menu items
- list selection
- progress bar updates
- task dialogs
- timers

## Status

This project is designed for small Windows tools first. API coverage is intentionally limited and focused on the parts that are most useful when you want a GUI without giving up Native AOT size goals.