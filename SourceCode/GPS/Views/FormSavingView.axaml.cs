// [XPLAT] migrated from net48/WinForms (Forms/FormSaving.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Per-step save progress dialog.
    ///
    /// 1:1 parity reimplementation of the WinForms <c>Forms/FormSaving</c> (FormSaving.cs +
    /// .Designer.cs). The WinForms form exposed three public methods used by the save pipeline —
    /// <see cref="AddStep"/>, <see cref="UpdateStep"/>, and <see cref="Finish"/> — that drove a
    /// <c>ListView</c> of steps, each prefixed with a bullet/check/cross glyph and coloured grey while
    /// pending then black once resolved, with a marquee progress bar that is hidden (revealing a
    /// "done" label) when saving finishes.
    ///
    /// This logic is entirely self-contained (it touches only the form's own controls), so it is
    /// ported verbatim. The WinForms <c>ListView</c> maps to the XAML <c>ItemsControl</c>
    /// (<c>listViewSteps</c>) whose DataTemplate binds <c>{Binding Text}</c> / <c>{Binding Foreground}</c>;
    /// the per-row backing object is <see cref="SavingStep"/>, which raises
    /// <see cref="INotifyPropertyChanged"/> so <see cref="UpdateStep"/> live-updates an existing row
    /// exactly as <c>listViewSteps.Items[key]</c> did. A key→row dictionary reproduces the WinForms
    /// keyed-item lookup.
    ///
    /// The WinForms ctor set <c>labelBeer.Text = "✔ " + gStr.gsSaveBeerTime</c>; localisation is
    /// handled application-wide in the Avalonia migration, so the caption is authored in the XAML and
    /// not re-bound to <c>gStr</c> here.
    /// </summary>
    public partial class FormSavingView : Window
    {
        private readonly Dictionary<string, SavingStep> _steps = new Dictionary<string, SavingStep>();

        public FormSavingView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// [XPLAT] Parity with FormSaving.AddStep: appends a pending step row keyed by
        /// <paramref name="key"/>, rendered grey with a leading bullet and a trailing ellipsis.
        /// </summary>
        public void AddStep(string key, string message)
        {
            var step = new SavingStep
            {
                Text = GetStepText(message + "...", SavingStepState.Pending),
                Foreground = GetStepColor(SavingStepState.Pending)
            };

            _steps[key] = step;
            listViewSteps.Items.Add(step);
        }

        /// <summary>
        /// [XPLAT] Parity with FormSaving.UpdateStep: updates the keyed row's text + colour to reflect
        /// the resolved <paramref name="state"/>.
        /// </summary>
        public void UpdateStep(string key, string message, SavingStepState state)
        {
            if (_steps.TryGetValue(key, out var step))
            {
                step.Text = GetStepText(message, state);
                step.Foreground = GetStepColor(state);
            }
        }

        /// <summary>
        /// [XPLAT] Parity with FormSaving.Finish: hides the marquee progress bar and reveals the
        /// completion label.
        /// </summary>
        public void Finish()
        {
            progressBar.IsVisible = false;
            labelBeer.IsVisible = true;
        }

        // [XPLAT] Parity with FormSaving.GetStepText: bullet (pending) / check (done) / cross (failed).
        private static string GetStepText(string message, SavingStepState state)
        {
            switch (state)
            {
                case SavingStepState.Pending:
                    return "• " + message;
                case SavingStepState.Done:
                    return "✓ " + message;
                case SavingStepState.Failed:
                    return "✗ " + message;
                default:
                    return message;
            }
        }

        // [XPLAT] Parity with FormSaving.GetStepColor: grey while pending, black once resolved.
        private static IBrush GetStepColor(SavingStepState state)
        {
            return state == SavingStepState.Pending ? Brushes.Gray : Brushes.Black;
        }

        /// <summary>
        /// [XPLAT] Observable backing object for a single <c>listViewSteps</c> row. The DataTemplate
        /// binds <c>Text</c> and <c>Foreground</c>, so both raise change notifications to support
        /// <see cref="UpdateStep"/> mutating a row already in the list.
        /// </summary>
        private sealed class SavingStep : INotifyPropertyChanged
        {
            private string _text;
            private IBrush _foreground;

            public string Text
            {
                get => _text;
                set
                {
                    if (_text != value)
                    {
                        _text = value;
                        OnPropertyChanged(nameof(Text));
                    }
                }
            }

            public IBrush Foreground
            {
                get => _foreground;
                set
                {
                    if (!Equals(_foreground, value))
                    {
                        _foreground = value;
                        OnPropertyChanged(nameof(Foreground));
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            private void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    /// <summary>
    /// [XPLAT] Parity with the WinForms <c>AgOpenGPS.SavingStepState</c> enum (FormSaving.cs).
    /// </summary>
    public enum SavingStepState
    {
        Pending,
        Done,
        Failed
    }
}
