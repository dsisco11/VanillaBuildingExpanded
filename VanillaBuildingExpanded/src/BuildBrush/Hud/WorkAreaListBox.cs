using System;
using System.Collections.Generic;

using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class WorkAreaListBox : GuiElementListBox<NamedWorkArea>
{
    public IReadOnlyList<NamedWorkArea> Areas => Items;
    public Guid? SelectedId => SelectedIndex >= 0 ? Items[SelectedIndex].Id : null;

    public WorkAreaListBox(ICoreClientAPI api, NamedWorkArea[] areas, Guid? selectedId, Action<Guid> selected,
        int firstVisible, bool revealSelection, Action<int> scrolled)
        : base(api, areas, area => area.Name, area => selected(area.Id), ElementBounds.Fixed(0, 0, 280, 0),
            selectedIndex: Array.FindIndex(areas, area => area.Id == selectedId),
            emptyText: Lang.Get("vanillabuildingexpanded:build-info-workarea-empty"),
            firstVisible: firstVisible, revealSelection: revealSelection, scrolled: scrolled)
    { }
}
