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
	/// Partage sa sélection de candidat avec <see cref="TryFindFirstFit"/> via
	/// <see cref="FindFirstFitCandidate"/> — seule cette méthode-ci mute réellement le conteneur
	/// (<see cref="AddPlacement"/>), <see cref="TryFindFirstFit"/> ne fait jamais cette dernière étape.
	/// </summary>
	public InventoryOperationResult TryAddFirstFit( ItemInstance item, bool allowRotation = true )
	{
		var result = FindFirstFitCandidate( item, allowRotation );
		if ( !result.Success )
			return result;

		AddPlacement( result.Placement );
		return result;
	}

	/// <summary>
	/// Préflight pur, jamais mutant : identique à <see cref="TryAddFirstFit"/> (même validation,
	/// même refus <see cref="InventoryFailureReason.AlreadyContained"/>, même ordre de scan —
	/// normale d'abord, tournée ensuite si autorisée et supportée), mais ne pose jamais le
	/// candidat trouvé dans le conteneur. Le placement candidat (position, rotation) reste
	/// disponible via <see cref="InventoryOperationResult.Placement"/> en cas de succès — pas de
	/// paramètres <c>out</c> séparés, cette information existe déjà là. Sert de première étape à
	/// une future transaction à deux conteneurs (transfert) qui doit savoir si la destination peut
	/// accepter l'item avant de retirer quoi que ce soit de la source — voir
	/// docs/architecture/WORLD_CONTAINER_ARCHITECTURE.md.
	/// </summary>
	public InventoryOperationResult TryFindFirstFit( ItemInstance item, bool allowRotation = true )
	{
		return FindFirstFitCandidate( item, allowRotation );
	}

	/// <summary>
	/// Sélection pure partagée par <see cref="TryAddFirstFit"/> et <see cref="TryFindFirstFit"/> —
	/// unique source de vérité pour « quel candidat de placement first-fit pour cet item, sous ces
	/// règles de rotation ». Ne mute jamais le conteneur : uniquement <see cref="ValidateItem"/>,
	/// le refus de doublon d'InstanceId, et <see cref="FindFirstFit"/> (elle-même pure, fondée sur
	/// <see cref="CanPlace"/>).
	/// </summary>
	InventoryOperationResult FindFirstFitCandidate( ItemInstance item, bool allowRotation )
	{
		var itemFailure = ValidateItem( item );
		if ( itemFailure != InventoryFailureReason.None )
			return InventoryOperationResult.Fail( itemFailure );

		if ( _byInstanceId.ContainsKey( item.InstanceId ) )
			return InventoryOperationResult.Fail( InventoryFailureReason.AlreadyContained );

		var fit = FindFirstFit( item, rotated: false );
		if ( fit.Success )
			return fit;

		if ( allowRotation && item.Definition.CanRotate )
		{
			fit = FindFirstFit( item, rotated: true );
			if ( fit.Success )
				return fit;
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

	/// <summary>
	/// Consomme <paramref name="amount"/> unités de l'instance visée — primitive canonique pour tout
	/// futur système d'utilisation d'item (ex. consommables). <b>Invariant central</b> :
	/// <see cref="ItemInstance.Quantity"/> ne peut jamais valoir zéro (voir
	/// <see cref="ItemInstance.TrySetQuantity"/>, qui refuse toute valeur &lt; 1) — il n'existe donc
	/// aucun état représentable où une instance aurait une quantité nulle. Cette méthode respecte cet
	/// invariant explicitement plutôt que de tenter une décrémentation qui échouerait silencieusement :
	/// si <paramref name="amount"/> épuise exactement la quantité disponible, le <em>placement</em> est
	/// retiré du conteneur (même effet que <see cref="TryRemove"/>) au lieu de tenter de mettre
	/// <see cref="ItemInstance.Quantity"/> à zéro. Si <paramref name="amount"/> est strictement inférieur
	/// à la quantité disponible, la même <see cref="ItemInstance"/> (même <see cref="ItemInstance.InstanceId"/>)
	/// est conservée, seule sa <see cref="ItemInstance.Quantity"/> diminue. Ne mute jamais la révision ni
	/// n'envoie de snapshot — ce conteneur reste un noyau pur sans réseau, ces responsabilités appartiennent
	/// à l'appelant (voir <see cref="Kodoku.Player.Inventory.PlayerInventoryComponent.NotifyMutated"/>).
	/// </summary>
	public InventoryOperationResult TryConsume( Guid instanceId, int amount = 1 )
	{
		if ( !_byInstanceId.TryGetValue( instanceId, out var placement ) )
			return InventoryOperationResult.Fail( InventoryFailureReason.ItemNotFound );

		if ( amount < 1 || amount > placement.Item.Quantity )
			return InventoryOperationResult.Fail( InventoryFailureReason.InvalidQuantity );

		if ( amount == placement.Item.Quantity )
		{
			// Consommation totale : retire le placement plutôt que de mettre Quantity à zéro
			// (état non représentable, voir commentaire de méthode ci-dessus).
			RemovePlacement( placement );
			return InventoryOperationResult.Ok( placement );
		}

		// Consommation partielle : amount < Quantity ici, donc strictement positif après retrait —
		// TryRemoveQuantity ne peut pas échouer. Même ItemInstance, même InstanceId, conservés tels quels.
		placement.Item.TryRemoveQuantity( amount );
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
