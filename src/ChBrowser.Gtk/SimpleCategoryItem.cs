using System;
using System.Collections.Generic;
using ChBrowser.Models;
using ChBrowser.Services.Render;

namespace ChBrowser.GtkApp;

public sealed class SimpleCategoryItem : IBoardCategoryTreeItem
{
    public string CategoryName { get; init; } = "";
    public string ProviderId { get; init; } = "";
    public bool IsExpanded { get; init; } = true;
    public IEnumerable<Board> Boards { get; init; } = Array.Empty<Board>();
}
