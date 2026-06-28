// [XPLAT] migrated from net48/WinForms (Forms/FormSaving.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Collections.ObjectModel;
using System.ComponentModel;
using AgOpenGPS.Core.Translations;
using Avalonia.Controls;
using Avalonia.Media;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Parity with the WinForms <c>AgOpenGPS.SavingStepState</c> enum (Forms/FormSaving.cs).
    /// Migrated verbatim; only the enclosing namespace moved (<c>AgOpenGPS</c> → <c>AgOpenGPS.Views</c>),
    /// so migrated callers reference <c>AgOpenGPS.Views.SavingStepState</c>.
    /// </summary>
    public enum SavingStepState
    {
        Pending,
        Done,
        Failed
    }

    /// <summary>
    /// [XPLAT] Modal "saving in progress" dialog — a 1:1 behavioral parity reimplementation of the
    /// WinForms <c>Forms/FormSaving</c> (FormSaving.cs + FormSaving.Designer.cs).
    /// </summary>
    /// <remarks>
    /// The WinForms form exposed three public methods used by the save pipeline —
    /// <see cref="AddStep"/>, <see cref="UpdateStep"/>, and <see cref="Finish"/> — that drove a keyed
    /// <c>ListView</c> of steps (each prefixed with a bullet/check/cross glyph and coloured grey while
    /// pending then black once resolved) above a marquee progress bar that is hidden — revealing a
    /// "beer time" completion label — when saving finishes. This logic is entirely self-contained
    /// (it touches only the dialog's own controls), so it is ported verbatim.
    /// <para>
    /// The WinForms <c>ListView</c> maps to the XAML <c>ItemsControl</c> (<c>listViewSteps</c>) whose
    /// DataTemplate binds <c>{Binding Text}</c> / <c>{Binding Foreground}</c>; the per-row backing
    /// object is <see cref="SavingStepItem"/>, which raises <see cref="INotifyPropertyChanged"/> so
    /// <see cref="UpdateStep"/> live-updates an existing row exactly as the WinForms
    /// <c>listViewSteps.Items[key]</c> indexer did, with <see cref="SavingStepItem.Key"/> reproducing
    /// the WinForms <c>ListViewItem.Name</c> used for keyed lookup.
    /// </para>
    /// </remarks>
    public partial class FormSavingView : Window
    {
        // [XPLAT] Backs listViewSteps. The WinForms keyed ListView.Items collection becomes an
        // ObservableCollection bound via ItemsSource; each row raises INotifyPropertyChanged so an
        // in-place UpdateStep refreshes the bound TextBlock without re-adding the item.
        private readonly ObservableCollection<SavingStepItem> _steps = new ObservableCollection<SavingStepItem>();

        /// <summary>
        /// [XPLAT] Parity with the WinForms <c>FormSaving()</c> constructor.
        /// </summary>
        public FormSavingView()
        {
            InitializeComponent();

            // [XPLAT] verbatim from WinForms FormSaving(): labelBeer.Text = "✔ " + gStr.gsSaveBeerTime.
            // gStr is the cross-platform translations helper (AgOpenGPS.Core.Translations); the .axaml
            // carries only a design-time default, so this runtime assignment supplies the localized text.
            labelBeer.Text = "✔ " + gStr.gsSaveBeerTime;

            // [XPLAT] WinForms populated ListView.Items directly; Avalonia binds the rows via ItemsSource.
            listViewSteps.ItemsSource = _steps;
        }

        /// <summary>
        /// [XPLAT] Parity with <c>FormSaving.AddStep</c>: appends a pending step row keyed by
        /// <paramref name="key"/>, rendered grey with a leading bullet and a trailing ellipsis.
        /// </summary>
        /// <param name="key">Stable identifier used by <see cref="UpdateStep"/> to locate the row.</param>
        /// <param name="message">Human-readable step label (the "..." suffix is appended for parity).</param>
        public void AddStep(string key, string message)
        {
            _steps.Add(new SavingStepItem
            {
                Key = key,
                Text = GetStepText(message + "...", SavingStepState.Pending),
                Foreground = GetStepColor(SavingStepState.Pending)
            });
        }

        /// <summary>
        /// [XPLAT] Parity with <c>FormSaving.UpdateStep</c>: locates the row whose
        /// <see cref="SavingStepItem.Key"/> matches <paramref name="key"/> and updates its text and
        /// colour to reflect the resolved <paramref name="state"/> (mirrors the WinForms
        /// <c>listViewSteps.Items[key]</c> keyed lookup). Unknown keys are ignored, matching the
        /// pipeline's add-then-update usage.
        /// </summary>
        public void UpdateStep(string key, string message, SavingStepState state)
        {
            foreach (SavingStepItem step in _steps)
            {
                if (step.Key == key)
                {
                    step.Text = GetStepText(message, state);
                    step.Foreground = GetStepColor(state);
                    break;
                }
            }
        }

        /// <summary>
        /// [XPLAT] Parity with <c>FormSaving.Finish</c>: hides the marquee progress bar and reveals the
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
                    throw new InvalidEnumArgumentException(nameof(state), (int)state, typeof(SavingStepState));
            }
        }

        // [XPLAT] Parity with FormSaving.GetStepColor: grey while pending, black once resolved.
        // System.Drawing.Color → Avalonia.Media.IBrush/Brushes (cross-platform).
        private static IBrush GetStepColor(SavingStepState state)
        {
            return state == SavingStepState.Pending ? Brushes.Gray : Brushes.Black;
        }

        /// <summary>
        /// [XPLAT] Observable backing object for a single <c>listViewSteps</c> row. The .axaml
        /// DataTemplate binds <c>{Binding Text}</c> and <c>{Binding Foreground}</c> by name (reflection
        /// bindings — the property names are the contract), so both raise change notifications to let
        /// <see cref="UpdateStep"/> mutate a row that is already in the list. <see cref="Key"/>
        /// reproduces the WinForms <c>ListViewItem.Name</c> used for keyed lookup.
        /// </summary>
        private sealed class SavingStepItem : INotifyPropertyChanged
        {
            private string _text;
            private IBrush _foreground;

            /// <summary>Stable lookup key (WinForms <c>ListViewItem.Name</c> equivalent).</summary>
            public string Key { get; set; }

            /// <summary>Glyph-prefixed step caption bound to the row's <c>TextBlock.Text</c>.</summary>
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

            /// <summary>Row foreground brush bound to the row's <c>TextBlock.Foreground</c>.</summary>
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
}
