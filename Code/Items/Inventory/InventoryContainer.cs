using System;
using Kodoku.Items;

namespace Kodoku.Items.Inventory;

/// <summary>
/// Noyau pur d'un conteneur d'inventaire à grille 2D : classe C# déterministe, sans réseau,
/// sans dépendance moteur (pas un <c>Component</c>, pas un <c>GameObject</c>). Ne crée jamais
/// de nouvelle <see cref="ItemInstance"/> — elle reste l'unique source de vérité pour
/// l'identité (<see cref="ItemInstance.InstanceId"/>) et les données statiques
/// (<see cref="ItemDefinition"/>). Voir docs/architecture/ITEM_ARCHITECTURE.md, section
/// "Inventory Core".
/// </summary>
public sealed class InventoryContainer
{
	public int Width { get; }

	public int Height { get; }

	readonly List<InventoryPlacement> _placements = new();
	readonly Dictionary<Guid, InventoryPlacement> _byInstanceId = new();

	/// <summary>Vue en lecture seule — la mutation ne passe que par les opérations atomiques ci-dessous.</summary>
	public IReadOnlyList<InventoryPlacement> Placements => _placements;

	public int Count => _placements.Count;

	/// <summary>
	/// Somme de <c>Item.Definition.Weight * Item.Quantity</c> sur tous les placements. Aucun
	/// poids maximal ni refus lié au poids dans ce jalon — voir docs/architecture/ITEM_ARCHITECTURE.md.
	/// </summary>
	public float CurrentWeight => _placements.Sum( p => p.Item.Definition.Weight * p.Item.Quantity );

	/// <summary>
	/// Une taille de conteneur invalide (Width/Height &lt;= 0) est une erreur de programmation,
	/// pas un échec de gameplay à récupérer — elle lève une exception plutôt que de retourner
	/// un résultat explicite.
	/// </summary>
	public InventoryContainer( int width, int height )
	{
		if ( width <= 0 )
			throw new ArgumentOutOfRangeException( nameof( width ), width, "Width must be greater than zero." );

		if ( height <= 0 )
			throw new ArgumentOutOfRangeException( nameof( height ), height, "Height must be greater than zero." );

		Width = width;
		Height = height;
	}

	public bool Contains( Guid instanceId ) => _byInstanceId.ContainsKey( instanceId );

	/// <summary>Retourne <c>null</c> si aucun placement ne correspond à cet <see cref="Guid"/>.</summary>
	public InventoryPlacement GetPlacement( Guid instanceId )
	{
		_byInstanceId.TryGetValue( instanceId, out var placement );
		return placement;
	}

	/// <summary>
	/// Valide un placement candidat sans muter le conteneur. <paramref name="ignoredInstanceId"/>
	/// exclut le placement existant de cette instance des vérifications de doublon/collision —
	/// utilisé par <see cref="TryMove"/> pour valider une nouvelle position tout en laissant
	/// l'ancienne inchangée tant que la cible n'est pas confirmée.
	/// </summary>
	public InventoryOperationResult CanPlace( ItemInstance item, int x, int y, bool rotated, Guid? ignoredInstanceId = null )
	{
		var itemFailure = ValidateItem( item );
		if ( itemFailure != InventoryFailureReason.None )
			return InventoryOperationResult.Fail( itemFailure );

		if ( rotated && !item.Definition.CanRotate )
			return InventoryOperationResult.Fail( InventoryFailureReason.RotationNotAllowed );

		var candidate = new InventoryPlacement( item, x, y, rotated );

		if ( candidate.X < 0 || candidate.Y < 0
			|| candidate.X + candidate.Width > Width
			|| candidate.Y + candidate.Height > Height )
			return InventoryOperationResult.Fail( InventoryFailureReason.OutOfBounds );

		bool isIgnored = ignoredInstanceId.HasValue && ignoredInstanceId.Value == item.InstanceId;

		if ( !isIgnored && _byInstanceId.ContainsKey( item.InstanceId ) )
			return InventoryOperationResult.Fail( InventoryFailureReason.AlreadyContained );

		foreach ( var existing in _placements )
		{
			if ( ignoredInstanceId.HasValue && existing.InstanceId == ignoredInstanceId.Value )
				continue;

			if ( candidate.Overlaps( existing ) )
				return InventoryOperationResult.Fail( InventoryFailureReason.Overlapping );
		}

		return InventoryOperationResult.Ok( candidate );
	}

	public InventoryOperationResult TryAdd( ItemInstance item, int x, int y, bool rotated = false )
	{
		var result = CanPlace( item, x, y, rotated );
		if ( !result.Success )
			return result;

		AddPlacement( result.Placement );
		return result;
	}

	/// <summary>
	/// Parcours déterministe : orientation normale d'abord, ligne par ligne, gauche à droite,
	/// haut en bas ; orientation tournée ensuite si autorisée et nécessaire. Jamais d'aléatoire.
	/// </summary>
	public InventoryOperationResult TryAddFirstFit( ItemInstance item, bool allowRotation = true )
	{
		var itemFailure = ValidateItem( item );
		if ( itemFailure != InventoryFailureReason.None )
			return InventoryOperationResult.Fail( itemFailure );

		if ( _byInstanceId.ContainsKey( item.InstanceId ) )
			return InventoryOperationResult.Fail( InventoryFailureReason.AlreadyContained );

		var fit = FindFirstFit( item, rotated: false );
		if ( fit.Success )
		{
			AddPlacement( fit.Placement );
			return fit;
		}

		if ( allowRotation && item.Definition.CanRotate )
		{
			fit = FindFirstFit( item, rotated: true );
			if ( fit.Success )
			{
				AddPlacement( fit.Placement );
				return fit;
			}
		}

		return InventoryOperationResult.Fail( InventoryFailureReason.NoAvailableSpace );
	}

	InventoryOperationResult FindFirstFit( ItemInstance item, bool rotated )
	{
		for ( int y = 0; y < Height; y++ )
		{
			for ( int x = 0; x < Width; x++ )
			{
				var result = CanPlace( item, x, y, rotated );
				if ( result.Success )
					return result;
			}
		}

		return InventoryOperationResult.Fail( InventoryFailureReason.NoAvailableSpace );
	}

	/// <summary>
	/// Atomique : la position/rotation actuelle reste strictement inchangée tant que la cible
	/// n'est pas validée dans son intégralité (limites, collisions hors de l'ancien placement,
	/// rotation autorisée). En cas d'échec, aucune mutation n'a lieu — pas de retrait suivi
	/// d'une tentative de réinsertion.
	/// </summary>
	public InventoryOperationResult TryMove( Guid instanceId, int targetX, int targetY, bool rotated )
	{
		if ( !_byInstanceId.TryGetValue( instanceId, out var current ) )
			return InventoryOperationResult.Fail( InventoryFailureReason.ItemNotFound );

		var result = CanPlace( current.Item, targetX, targetY, rotated, ignoredInstanceId: instanceId );
		if ( !result.Success )
			return result;

		RemovePlacement( current );
		AddPlacement( result.Placement );
		return result;
	}

	/// <summary>Retire exactement l'instance visée sans la détruire ni modifier sa <see cref="ItemInstance.Quantity"/>.</summary>
	public InventoryOperationResult TryRemove( Guid instanceId, out ItemInstance removedItem )
	{
		if ( !_byInstanceId.TryGetValue( instanceId, out var placement ) )
		{
			removedItem = null;
			return InventoryOperationResult.Fail( InventoryFailureReason.ItemNotFound );
		}

		RemovePlacement( placement );
		removedItem = placement.Item;
		return InventoryOperationResult.Ok( placement );
	}

	void AddPlacement( InventoryPlacement placement )
	{
		_placements.Add( placement );
		_byInstanceId[placement.InstanceId] = placement;
	}

	void RemovePlacement( InventoryPlacement placement )
	{
		_placements.Remove( placement );
		_byInstanceId.Remove( placement.InstanceId );
	}

	static InventoryFailureReason ValidateItem( ItemInstance item )
	{
		if ( item is null )
			return InventoryFailureReason.InvalidItem;

		if ( item.InstanceId == Guid.Empty )
			return InventoryFailureReason.InvalidInstanceId;

		var definition = item.Definition;
		if ( definition is null || string.IsNullOrEmpty( definition.ItemId ) )
			return InventoryFailureReason.InvalidDefinition;

		if ( definition.GridWidth < 1 || definition.GridHeight < 1 )
			return InventoryFailureReason.InvalidItemDimensions;

		if ( item.Quantity < 1 || item.Quantity > definition.MaxStack )
			return InventoryFailureReason.InvalidQuantity;

		return InventoryFailureReason.None;
	}

	/// <summary>
	/// Vérification interne des invariants (unicité des <see cref="Guid"/>, limites, absence de
	/// chevauchement, cohérence entre la liste et l'index). Réservée à l'outillage de debug —
	/// ne mute jamais l'état ; coût en O(n²) sur les placements, négligeable à l'échelle visée
	/// par ce jalon.
	/// </summary>
	internal bool TryValidateState( out string error )
	{
		if ( _placements.Count != _byInstanceId.Count )
		{
			error = $"Placements count ({_placements.Count}) does not match index count ({_byInstanceId.Count}).";
			return false;
		}

		var seen = new HashSet<Guid>();

		for ( int i = 0; i < _placements.Count; i++ )
		{
			var placement = _placements[i];

			if ( !seen.Add( placement.InstanceId ) )
			{
				error = $"Duplicate InstanceId '{placement.InstanceId}' found in placements.";
				return false;
			}

			if ( placement.X < 0 || placement.Y < 0
				|| placement.X + placement.Width > Width
				|| placement.Y + placement.Height > Height )
			{
				error = $"Placement '{placement.InstanceId}' is out of bounds.";
				return false;
			}

			if ( !_byInstanceId.TryGetValue( placement.InstanceId, out var indexed ) || !ReferenceEquals( indexed, placement ) )
			{
				error = $"Placement '{placement.InstanceId}' is inconsistent with the InstanceId index.";
				return false;
			}

			for ( int j = i + 1; j < _placements.Count; j++ )
			{
				if ( placement.Overlaps( _placements[j] ) )
				{
					error = $"Placement '{placement.InstanceId}' overlaps with '{_placements[j].InstanceId}'.";
					return false;
				}
			}
		}

		error = "";
		return true;
	}
}
