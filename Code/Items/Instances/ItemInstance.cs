using System;

namespace Kodoku.Items;

/// <summary>
/// État runtime d'un exemplaire précis d'un <see cref="ItemDefinition"/> : identité stable,
/// définition et quantité — rien d'autre (voir docs/architecture/ITEM_ARCHITECTURE.md pour
/// la liste explicite des données exclues). La création de nouvelles instances sera
/// contrôlée par l'autorité host — non implémenté ici, voir <see cref="CreateNew"/>.
/// </summary>
public sealed class ItemInstance
{
	/// <summary>
	/// Identifiant stable de cet exemplaire, indépendant de <see cref="ItemDefinition.ItemId"/>.
	/// Généré une fois à la création, conservé tel quel à la restauration.
	/// </summary>
	public Guid InstanceId { get; }

	public ItemDefinition Definition { get; }

	public int Quantity { get; private set; }

	ItemInstance( Guid instanceId, ItemDefinition definition, int quantity )
	{
		if ( definition is null )
			throw new ArgumentNullException( nameof( definition ) );

		if ( string.IsNullOrEmpty( definition.ItemId ) )
			throw new ArgumentException( "Definition.ItemId must not be empty.", nameof( definition ) );

		if ( instanceId == Guid.Empty )
			throw new ArgumentException( "InstanceId must not be Guid.Empty.", nameof( instanceId ) );

		if ( quantity < 1 || quantity > definition.MaxStack )
			throw new ArgumentOutOfRangeException( nameof( quantity ), quantity, $"Quantity must be between 1 and Definition.MaxStack ({definition.MaxStack})." );

		InstanceId = instanceId;
		Definition = definition;
		Quantity = quantity;
	}

	/// <summary>Crée un nouvel exemplaire avec un <see cref="InstanceId"/> fraîchement généré.</summary>
	public static ItemInstance CreateNew( ItemDefinition definition, int quantity = 1 )
	{
		return new ItemInstance( Guid.NewGuid(), definition, quantity );
	}

	/// <summary>
	/// Reconstruit un exemplaire existant en conservant son <see cref="InstanceId"/> fourni.
	/// Pour une future sauvegarde/réplication/late join — ces systèmes ne sont pas implémentés
	/// ici, seul ce point d'entrée est préparé.
	/// </summary>
	public static ItemInstance Restore( Guid instanceId, ItemDefinition definition, int quantity )
	{
		return new ItemInstance( instanceId, definition, quantity );
	}

	public bool TrySetQuantity( int quantity )
	{
		if ( quantity < 1 || quantity > Definition.MaxStack )
			return false;

		Quantity = quantity;
		return true;
	}

	public bool TryAddQuantity( int amount )
	{
		if ( amount <= 0 )
			return false;

		return TrySetQuantity( Quantity + amount );
	}

	public bool TryRemoveQuantity( int amount )
	{
		if ( amount <= 0 )
			return false;

		return TrySetQuantity( Quantity - amount );
	}

	/// <summary>
	/// Vrai si ces deux exemplaires pourraient être empilés — ne fusionne rien, se contente
	/// de répondre à la question. Le transfert de quantité réel appartient à un futur système
	/// d'inventaire.
	/// </summary>
	public bool CanStackWith( ItemInstance other )
	{
		if ( other is null )
			return false;

		if ( Definition is null || other.Definition is null )
			return false;

		if ( Definition.MaxStack <= 1 )
			return false;

		if ( InstanceId == other.InstanceId )
			return false;

		return Definition == other.Definition || Definition.ItemId == other.Definition.ItemId;
	}
}
