// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections;
using System.Globalization;

namespace AgIO
{
    /// <summary>
    /// Sort order state. Portable replacement for System.Windows.Forms.SortOrder.
    /// </summary>
    public enum ListSortState
    {
        None,
        Ascending,
        Descending
    }

    /// <summary>
    /// Framework-agnostic, culture-invariant, type-aware column sorter.
    /// Migrated from a WinForms ListView IComparer. The consuming Avalonia view-model
    /// extracts the active sort column's cell text for each row and uses this comparer
    /// (via Compare(object,object) on the cell-text values, or by calling CompareText directly).
    /// </summary>
    public class ListViewColumnSorterExt : IComparer
    {
        /// <summary>
        /// Case insensitive comparer object (InvariantCulture for cross-OS deterministic ordering).
        /// </summary>
        private CaseInsensitiveComparer ObjectCompare;

        public ListViewColumnSorterExt()
        {
            // Initialize the column to '0'
            SortColumn = 0;

            // Initialize the sort order to 'none'
            Order = ListSortState.None;

            // Initialize the CaseInsensitiveComparer object (InvariantCulture)
            ObjectCompare = new CaseInsensitiveComparer(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Gets or sets the number of the column to which to apply the sorting operation (Defaults to '0').
        /// </summary>
        public int SortColumn { set; get; }

        /// <summary>
        /// Gets or sets the order of sorting to apply (for example, 'Ascending' or 'Descending').
        /// </summary>
        public ListSortState Order { set; get; }

        /// <summary>
        /// IComparer implementation. Compares the two already-extracted cell-text values.
        /// </summary>
        public int Compare(object x, object y)
        {
            return CompareText(
                Convert.ToString(x, CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(y, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        /// <summary>
        /// Type-aware, culture-invariant comparison of two cell-text strings, with tri-state Order applied.
        /// </summary>
        public int CompareText(string x, string y)
        {
            int compareResult;

            if (decimal.TryParse(x, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal dx) &&
                decimal.TryParse(y, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal dy))
            {
                // compare the 2 items as numbers
                compareResult = decimal.Compare(dx, dy);
            }
            else if (DateTime.TryParse(x, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dtx) &&
                     DateTime.TryParse(y, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dty))
            {
                // compare the 2 items as dates
                compareResult = DateTime.Compare(dtx, dty);
            }
            // When one is a number and the other not, return -1 to have the numbers on top (or bottom)
            else if (decimal.TryParse(x, NumberStyles.Number, CultureInfo.InvariantCulture, out dx))
            {
                compareResult = -1;
            }
            // When one is a number and the other not, return 1 to have the numbers on top (or bottom)
            else if (decimal.TryParse(y, NumberStyles.Number, CultureInfo.InvariantCulture, out dy))
            {
                compareResult = 1;
            }
            else
            {
                // Compare the two items as case-insensitive strings
                compareResult = ObjectCompare.Compare(x, y);
            }

            // Calculate correct return value based on object comparison
            if (Order == ListSortState.Ascending)
            {
                // Ascending sort is selected, return normal result of compare operation
                return compareResult;
            }
            else if (Order == ListSortState.Descending)
            {
                // Descending sort is selected, return negative result of compare operation
                return (-compareResult);
            }
            else
            {
                // Return '0' to indicate they are equal
                return 0;
            }
        }

        /// <summary>
        /// Toggles sort direction for the clicked column (ascending&lt;-&gt;descending), or selects a new
        /// column ascending. The consuming view re-applies the sort afterward.
        /// </summary>
        public void ReverseSortOrderAndSort(int column)
        {
            // Determine if clicked column is already the column that is being sorted.
            if (column == SortColumn)
            {
                // Reverse the current sort direction for this column.
                if (Order == ListSortState.Ascending)
                {
                    Order = ListSortState.Descending;
                }
                else
                {
                    Order = ListSortState.Ascending;
                }
            }
            else
            {
                // Set the column number that is to be sorted; default to ascending.
                SortColumn = column;
                Order = ListSortState.Ascending;
            }
        }
    }
}
