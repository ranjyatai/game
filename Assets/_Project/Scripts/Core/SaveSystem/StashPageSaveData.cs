using System;
using System.Collections.Generic;

[Serializable]
public class StashPageSaveData
{
    public int                  pageIndex = 0;
    public List<SavedItemEntry> items     = new List<SavedItemEntry>();
}
