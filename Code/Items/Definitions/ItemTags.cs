using System;

namespace Kodoku.Items;

/// <summary>
/// Étiquettes combinables d'un <see cref="ItemDefinition"/> (filtrage/requêtes futures,
/// ex. "tout ce qui est buvable"). Volontairement minimal — étendre uniquement quand un
/// besoin réel apparaît, pas par anticipation.
/// </summary>
[Flags]
public enum ItemTags
{
	None = 0,
	Drink = 1 << 0,
	Water = 1 << 1,
}
