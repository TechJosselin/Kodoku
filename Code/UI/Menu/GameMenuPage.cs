namespace Kodoku.UI;

/// <summary>
/// Page sélectionnable du <see cref="GameMenu"/> — source de vérité de la navigation, jamais une
/// chaîne de caractères (voir <see cref="GameMenu.ActivePage"/>). Volontairement sans <c>Map</c> :
/// seules les quatre pages de ce jalon existent.
/// </summary>
public enum GameMenuPage
{
	Inventory,
	Stats,
	Quests,
	Options,
}
