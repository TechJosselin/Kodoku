using System;
using Kodoku.Items;

namespace Kodoku.Items.Inventory;

/// <summary>
/// Relation spatiale immuable entre une <see cref="ItemInstance"/> existante et une position
/// dans un <see cref="InventoryContainer"/>. Ne stocke aucune donnée déjà portée par
/// <see cref="ItemInstance"/>/<see cref="ItemDefinition"/> (ItemId, Quantity, Weight,
/// GridWidth/GridHeight, CanRotate) — ces informations restent lues via <see cref="Item"/>.
/// Un déplacement ou une rotation remplace ce placement par une nouvelle instance de cette
/// classe, il n'est jamais modifié en place.
/// </summary>
public sealed class InventoryPlacement
{
	public ItemInstance Item { get; }

	public Guid InstanceId => Item.InstanceId;

	public int X { get; }

	public int Y { get; }

	public bool IsRotated { get; }

	public int Width { get; }

	public int Height { get; }

	public InventoryPlacement( ItemInstance item, int x, int y, bool isRotated )
	{
		if ( item is null )
			throw new ArgumentNullException( nameof( item ) );

		Item = item;
		X = x;
		Y = y;
		IsRotated = isRotated;

		var definition = item.Definition;
		Width = isRotated ? definition.GridHeight : definition.GridWidth;
		Height = isRotated ? definition.GridWidth : definition.GridHeight;
	}

	/// <summary>Test de collision rectangle contre rectangle, indépendant de toute grille secondaire.</summary>
	public bool Overlaps( InventoryPlacement other )
	{
		if ( other is null )
			return false;

		return X < other.X + other.Width && other.X < X + Width
			&& Y < other.Y + other.Height && other.Y < Y + Height;
	}
}
