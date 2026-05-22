using Spectre.Console;
using ZapretManager.Core;
using SpectreColor = Spectre.Console.Color;

namespace ZapretManager.UI;

/// <summary>
/// Rich console UI built on Spectre.Console.
/// All write helpers also log to the file logger.
/// </summary>
public static class ConsoleMenu
{
    // ── Core write helpers ───────────────────────────────────────────────────

    /// <summary>Print a bold section header with a horizontal rule.</summary>
    public static void WriteHeader(string title)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold cyan]{Markup.Escape(title)}[/]")
            .RuleStyle("cyan dim").LeftJustified());
        AnsiConsole.WriteLine();
        Logger.Step(title);
    }

    /// <summary>Step / action prefix line (yellow arrow).</summary>
    public static void WriteStep(string msg)
    {
        AnsiConsole.MarkupLine($"[yellow]  >> [/][bold]{Markup.Escape(msg)}[/]");
        Logger.Step(msg);
    }

    /// <summary>Success line (green tick).</summary>
    public static void WriteOk(string msg)
    {
        AnsiConsole.MarkupLine($"[green]  ✔  {Markup.Escape(msg)}[/]");
        Logger.Ok(msg);
    }

    /// <summary>Warning line (yellow exclamation).</summary>
    public static void WriteWarn(string msg)
    {
        AnsiConsole.MarkupLine($"[yellow]  !  {Markup.Escape(msg)}[/]");
        Logger.Warn(msg);
    }

    /// <summary>Error line (red cross).</summary>
    public static void WriteError(string msg)
    {
        AnsiConsole.MarkupLine($"[red]  ✘  {Markup.Escape(msg)}[/]");
        Logger.Error(msg);
    }

    /// <summary>Neutral info line (grey dash).</summary>
    public static void WriteInfo(string msg)
    {
        AnsiConsole.MarkupLine($"[grey]  –  {Markup.Escape(msg)}[/]");
        Logger.Info(msg);
    }

    /// <summary>Thin separator rule.</summary>
    public static void WriteSeparator() =>
        AnsiConsole.Write(new Rule().RuleStyle("grey dim"));

    // ── Status table ─────────────────────────────────────────────────────────

    /// <summary>
    /// Render a two-column status table (label → value with colour markup).
    /// <paramref name="rows"/> format: (label, markupValue) pairs.
    /// </summary>
    public static void WriteStatusTable(IEnumerable<(string Label, string MarkupValue)> rows)
    {
        var table = new Table()
            .NoBorder()
            .HideHeaders()
            .AddColumn(new TableColumn("[dim]Key[/]").Width(22).PadLeft(4))
            .AddColumn(new TableColumn("[dim]Value[/]"));

        foreach (var (label, value) in rows)
            table.AddRow($"[dim]{Markup.Escape(label)}[/]", value);

        AnsiConsole.Write(table);
    }

    // ── Selection prompt ─────────────────────────────────────────────────────

    /// <summary>
    /// Show an interactive selection prompt. Returns the chosen item.
    /// Falls back to numbered list + ReadLine when no interactive terminal.
    /// </summary>
    public static T SelectionPrompt<T>(string title, IEnumerable<T> choices,
        Func<T, string>? display = null) where T : notnull
    {
        var items = choices.ToList();
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            // Fallback for non-interactive (e.g. redirected stdin)
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(title)}[/]");
            for (int i = 0; i < items.Count; i++)
                AnsiConsole.MarkupLine($"  [cyan]{i + 1,2}.[/] {Markup.Escape(display?.Invoke(items[i]) ?? items[i].ToString()!)}");
            AnsiConsole.Markup("  Enter number: ");
            var raw = Console.ReadLine()?.Trim();
            if (int.TryParse(raw, out var idx) && idx >= 1 && idx <= items.Count)
                return items[idx - 1];
            return items[0];
        }

        var prompt = new SelectionPrompt<T>()
            .Title($"[bold]{Markup.Escape(title)}[/]")
            .PageSize(20)
            .HighlightStyle(new Style(foreground: SpectreColor.Cyan1, decoration: Decoration.Bold))
            .UseConverter(display ?? (x => x.ToString()!));

        prompt.AddChoices(items);
        return AnsiConsole.Prompt(prompt);
    }

    /// <summary>
    /// Multi-select checkbox prompt. Returns selected items.
    /// </summary>
    public static List<T> MultiSelectionPrompt<T>(string title, IEnumerable<T> choices,
        Func<T, string>? display = null) where T : notnull
    {
        var items = choices.ToList();
        var prompt = new MultiSelectionPrompt<T>()
            .Title($"[bold]{Markup.Escape(title)}[/]")
            .PageSize(20)
            .NotRequired()
            .HighlightStyle(new Style(foreground: SpectreColor.Cyan1, decoration: Decoration.Bold))
            .InstructionsText("[grey](Пробел — выбрать, Enter — подтвердить)[/]")
            .UseConverter(display ?? (x => x.ToString()!));

        prompt.AddChoices(items);
        return AnsiConsole.Prompt(prompt);
    }

    // ── Spinner ──────────────────────────────────────────────────────────────

    private static CancellationTokenSource? _spinCts;
    private static Task? _spinTask;

    /// <summary>Start an async Spectre spinner. Call <see cref="StopSpinner"/> when done.</summary>
    public static void StartSpinner(string msg)
    {
        _spinCts = new CancellationTokenSource();
        var tok = _spinCts.Token;

        if (AnsiConsole.Profile.Capabilities.Interactive)
        {
            _spinTask = Task.Run(async () =>
            {
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("cyan"))
                    .StartAsync(Markup.Escape(msg), async ctx =>
                    {
                        try { await Task.Delay(Timeout.Infinite, tok); }
                        catch (OperationCanceledException) { }
                    });
            }, CancellationToken.None);
        }
        else
        {
            // Non-interactive: just print the message
            AnsiConsole.MarkupLine($"[cyan]  ⟳  {Markup.Escape(msg)}[/]");
        }
    }

    /// <summary>Stop the spinner and optionally print result.</summary>
    public static void StopSpinner(bool ok = true, string? result = null)
    {
        _spinCts?.Cancel();
        try { _spinTask?.Wait(800); }
        catch (AggregateException) { }
        catch (OperationCanceledException) { }
        _spinCts = null;
        _spinTask = null;

        if (result != null)
        {
            if (ok) WriteOk(result);
            else WriteError(result);
        }
    }

    // ── Progress bar ─────────────────────────────────────────────────────────

    /// <summary>Write a progress bar line (non-interactive friendly).</summary>
    public static void WriteProgress(string label, int current, int total)
    {
        const int barWidth = 30;
        var pct    = total > 0 ? (double)current / total : 0;
        var filled = (int)(pct * barWidth);
        var bar    = new string('█', filled) + new string('░', barWidth - filled);

        if (AnsiConsole.Profile.Capabilities.Interactive)
        {
            AnsiConsole.Markup($"\r[cyan]   [[{Markup.Escape(bar)}]][/] {current}/{total}  [dim]{Markup.Escape(label.PadRight(30))}[/]");
            if (current >= total) AnsiConsole.WriteLine();
        }
        else
        {
            if (current >= total)
                AnsiConsole.MarkupLine($"[cyan]  [[{Markup.Escape(bar)}]][/] {current}/{total}  {Markup.Escape(label)}");
        }
    }

    // ── Input helpers ────────────────────────────────────────────────────────

    /// <summary>Prompt the user for text input with optional default value.</summary>
    public static string? Prompt(string prompt, string? defaultValue = null)
    {
        string? input;
        if (AnsiConsole.Profile.Capabilities.Interactive)
        {
            var tp = new TextPrompt<string>($"  [white]{Markup.Escape(prompt)}[/]")
                .AllowEmpty();
            if (defaultValue != null)
                tp.DefaultValue(defaultValue).DefaultValueStyle(Style.Parse("dim"));
            input = AnsiConsole.Prompt(tp);
        }
        else
        {
            if (defaultValue != null)
                AnsiConsole.Markup($"  {Markup.Escape(prompt)} [[{Markup.Escape(defaultValue)}]]: ");
            else
                AnsiConsole.Markup($"  {Markup.Escape(prompt)}: ");
            input = Console.ReadLine()?.Trim();
        }

        return string.IsNullOrEmpty(input) ? defaultValue : input;
    }

    /// <summary>
    /// Prompt with optional inline validation loop (4.3).
    /// Keeps asking until <paramref name="validator"/> returns <c>true</c> or input is empty/default.
    /// </summary>
    public static string? Prompt(
        string prompt,
        string? defaultValue,
        Func<string, bool>? validator,
        string? validationError = null)
    {
        while (true)
        {
            var input = Prompt(prompt, defaultValue);
            if (validator == null) return input;
            if (string.IsNullOrEmpty(input)) return input;  // empty/default passes through
            if (validator(input)) return input;
            WriteError(validationError ?? "Неверный ввод. Попробуйте ещё раз.");
        }
    }

    /// <summary>
    /// Interactive list picker using Spectre SelectionPrompt (3.3).
    /// Falls back to index-based selection in non-interactive mode.
    /// </summary>
    public static T PickFromList<T>(
        string title,
        IEnumerable<T> items,
        Func<T, string>? display = null) where T : notnull
    {
        var list = items.ToList();
        if (AnsiConsole.Profile.Capabilities.Interactive)
        {
            return AnsiConsole.Prompt(
                new SelectionPrompt<T>()
                    .Title($"[bold]{Markup.Escape(title)}[/]")
                    .PageSize(12)
                    .HighlightStyle(new Style(foreground: SpectreColor.Cyan1))
                    .UseConverter(display ?? (x => x.ToString()!))
                    .AddChoices(list));
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(title)}[/]");
            for (int i = 0; i < list.Count; i++)
                AnsiConsole.MarkupLine($"  [dim]{i + 1}.[/] {Markup.Escape((display ?? (x => x.ToString()!))(list[i]))}");
            while (true)
            {
                var raw = Prompt("Введите номер");
                if (int.TryParse(raw, out var idx) && idx >= 1 && idx <= list.Count)
                    return list[idx - 1];
                WriteError($"Введите число от 1 до {list.Count}");
            }
        }
    }

    /// <summary>
    /// Multi-select picker using Spectre MultiSelectionPrompt (3.3).
    /// </summary>
    public static List<T> PickMultiple<T>(
        string title,
        IEnumerable<T> items,
        Func<T, string>? display = null) where T : notnull
    {
        var list = items.ToList();
        if (AnsiConsole.Profile.Capabilities.Interactive)
        {
            return AnsiConsole.Prompt(
                new MultiSelectionPrompt<T>()
                    .Title($"[bold]{Markup.Escape(title)}[/]")
                    .PageSize(12)
                    .NotRequired()
                    .HighlightStyle(new Style(foreground: SpectreColor.Cyan1, decoration: Decoration.Bold))
                    .InstructionsText("[grey](Пробел — выбрать, Enter — подтвердить)[/]")
                    .UseConverter(display ?? (x => x.ToString()!))
                    .AddChoices(list));
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(title)}[/] [dim](через запятую, Enter = все)[/]");
            for (int i = 0; i < list.Count; i++)
                AnsiConsole.MarkupLine($"  [dim]{i + 1}.[/] {Markup.Escape((display ?? (x => x.ToString()!))(list[i]))}");
            var raw = Prompt("Номера (через запятую)");
            if (string.IsNullOrWhiteSpace(raw)) return list;
            var selected = new List<T>();
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(part.Trim(), out var idx) && idx >= 1 && idx <= list.Count)
                    selected.Add(list[idx - 1]);
            return selected;
        }
    }

    /// <summary>Yes/No confirmation prompt.</summary>
    public static bool Confirm(string prompt)
    {
        if (AnsiConsole.Profile.Capabilities.Interactive)
            return AnsiConsole.Confirm($"  {Markup.Escape(prompt)}", defaultValue: false);

        AnsiConsole.Markup($"  [white]{Markup.Escape(prompt)}[/] [dim][д/н][/]: ");
        var key = Console.ReadKey(intercept: true);
        AnsiConsole.WriteLine();
        return key.KeyChar is 'д' or 'Д' or 'y' or 'Y';
    }

    /// <summary>Wait for any key press.</summary>
    public static void PauseAny(string msg = "Нажмите любую клавишу для продолжения...")
    {
        AnsiConsole.MarkupLine($"\n  [dim]{Markup.Escape(msg)}[/]");
        if (AnsiConsole.Profile.Capabilities.Interactive)
            Console.ReadKey(true);
        else
            Console.ReadLine();
    }
}

// ── Internal logger alias ────────────────────────────────────────────────────
file static class Logger
{
    public static void Step(string m)  => Core.Logger.Step(m);
    public static void Ok(string m)    => Core.Logger.Ok(m);
    public static void Warn(string m)  => Core.Logger.Warn(m);
    public static void Error(string m) => Core.Logger.Error(m);
    public static void Info(string m)  => Core.Logger.Info(m);
}
