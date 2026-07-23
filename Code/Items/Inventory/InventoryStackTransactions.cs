using System;

namespace Kodoku.Items.Inventory;

/// <summary>
/// Couche C# pure, sans dépendance à s&amp;box (pas un <c>Component</c>, pas de RPC, pas de
/// notion de viewer/révision/RequestId), orchestrant les transactions de quantité portant sur un
/// ou deux <see cref="InventoryContainer"/> : split, merge (exact et jusqu'à capacité), transfert
/// partiel vers une case vide. Applique la politique d'identité et l'atomicité/rollback décrites
/// dans docs/architecture/INVENTORY_STACK_ARCHITECTURE.md (sections 3 et 5) — ne réimplémente
/// aucune validation spatiale ou de quantité déjà couverte par <see cref="InventoryContainer"/>,
/// elle les compose. Ne crée jamais elle-même de nouvelle <see cref="ItemInstance"/> — l'appelant
/// fournit toujours l'instance préparée nécessaire à un split ou un transfert partiel.
/// </summary>
public static class InventoryStackTransactions
{
	// --- Split (un seul conteneur) ---

	/// <summary>Split first-fit : extrait <paramref name="amount"/> unités vers un emplacement libre quelconque du même conteneur.</summary>
	public static StackTransactionResult TrySplit( InventoryContainer container, Guid sourceInstanceId, int amount, ItemInstance preparedInstance, bool allowRotation = true )
		=> TransferPartialToEmptyCore( container, container, sourceInstanceId, amount, preparedInstance, targeted: false, 0, 0, false, allowRotation );

	/// <summary>Split ciblé : extrait <paramref name="amount"/> unités vers une position précise du même conteneur.</summary>
	public static StackTransactionResult TrySplitAt( InventoryContainer container, Guid sourceInstanceId, int amount, ItemInstance preparedInstance, int targetX, int targetY, bool targetRotated )
		=> TransferPartialToEmptyCore( container, container, sourceInstanceId, amount, preparedInstance, targeted: true, targetX, targetY, targetRotated, allowRotation: false );

	// --- Transfert partiel vers une case vide (un ou deux conteneurs) ---

	/// <summary>
	/// Transfère <paramref name="amount"/> unités de <paramref name="sourceInstanceId"/> (dans
	/// <paramref name="sourceContainer"/>) vers un emplacement libre quelconque de
	/// <paramref name="targetContainer"/>, first-fit. Fonctionne aussi bien entre deux conteneurs
	/// distincts qu'avec <paramref name="sourceContainer"/> == <paramref name="targetContainer"/>
	/// (dans ce cas, strictement équivalent à <see cref="TrySplit"/>).
	/// </summary>
	public static StackTransactionResult TryTransferPartialToEmpty( InventoryContainer sourceContainer, InventoryContainer targetContainer, Guid sourceInstanceId, int amount, ItemInstance preparedInstance, bool allowRotation = true )
		=> TransferPartialToEmptyCore( sourceContainer, targetContainer, sourceInstanceId, amount, preparedInstance, targeted: false, 0, 0, false, allowRotation );

	/// <summary>Variante ciblée de <see cref="TryTransferPartialToEmpty"/> — position précise dans <paramref name="targetContainer"/>.</summary>
	public static StackTransactionResult TryTransferPartialToEmptyAt( InventoryContainer sourceContainer, InventoryContainer targetContainer, Guid sourceInstanceId, int amount, ItemInstance preparedInstance, int targetX, int targetY, bool targetRotated )
		=> TransferPartialToEmptyCore( sourceContainer, targetContainer, sourceInstanceId, amount, preparedInstance, targeted: true, targetX, targetY, targetRotated, allowRotation: false );

	/// <summary>
	/// Ordre des validations (aucune mutation avant la fin de cette étape) : source existante,
	/// <c>1 &lt;= amount &lt;= source.Quantity - 1</c> (structurellement impossible pour
	/// <c>MaxStack == 1</c>, aucune branche dédiée nécessaire), instance préparée valide et absente
	/// des deux conteneurs, destination valide. Mutations ensuite (décrément source, ajout
	/// destination) — un échec inattendu de la seconde mutation restaure la quantité source
	/// (<see cref="InventoryContainer.TryGrowQuantity"/>), jamais un split/transfert n'observe la
	/// pile source déjà retirée sans que la nouvelle pile n'existe.
	/// </summary>
	static StackTransactionResult TransferPartialToEmptyCore(
		InventoryContainer sourceContainer,
		InventoryContainer targetContainer,
		Guid sourceInstanceId,
		int amount,
		ItemInstance preparedInstance,
		bool targeted,
		int targetX,
		int targetY,
		bool targetRotated,
		bool allowRotation )
	{
		if ( sourceContainer is null )
			return StackTransactionResult.Fail( StackTransactionFailureReason.SourceNotFound, sourceInstanceId, null, amount );

		if ( targetContainer is null )
			return StackTransactionResult.Fail( StackTransactionFailureReason.TargetNotFound, sourceInstanceId, null, amount );

		var source = sourceContainer.GetPlacement( sourceInstanceId );
		if ( source is null )
			return StackTransactionResult.Fail( StackTransactionFailureReason.SourceNotFound, sourceInstanceId, null, amount );

		if ( amount < 1 || amount >= source.Item.Quantity )
			return StackTransactionResult.Fail( StackTransactionFailureReason.InvalidAmount, sourceInstanceId, null, amount );

		if ( preparedInstance is null
			|| preparedInstance.InstanceId == Guid.Empty
			|| preparedInstance.InstanceId == sourceInstanceId
			|| preparedInstance.Quantity != amount
			|| !source.Item.CanStackWith( preparedInstance ) )
			return StackTransactionResult.Fail( StackTransactionFailureReason.InvalidPreparedInstance, sourceInstanceId, null, amount );

		if ( sourceContainer.Contains( preparedInstance.InstanceId ) || targetContainer.Contains( preparedInstance.InstanceId ) )
			return StackTransactionResult.Fail( StackTransactionFailureReason.PreparedInstanceAlreadyPresent, sourceInstanceId, null, amount );

		var destinationCheck = targeted
			? targetContainer.CanPlace( preparedInstance, targetX, targetY, targetRotated )
			: targetContainer.TryFindFirstFit( preparedInstance, allowRotation );

		if ( !destinationCheck.Success )
			return StackTransactionResult.Fail( StackTransactionFailureReason.DestinationInvalid, sourceInstanceId, null, amount, destinationCheck.FailureReason );

		var placementCandidate = destinationCheck.Placement;

		// État initial validé avant toute mutation — défense en profondeur, voir ValidateAffectedContainers.
		if ( !ValidateAffectedContainers( sourceContainer, targetContainer ) )
			return StackTransactionResult.Fail( StackTransactionFailureReason.InvariantViolation, sourceInstanceId, null, amount );

		var consumeResult = sourceContainer.TryConsume( sourceInstanceId, amount );
		if ( !consumeResult.Success )
			return StackTransactionResult.Fail( StackTransactionFailureReason.UnexpectedMutationFailure, sourceInstanceId, null, amount );

		var addResult = targetContainer.TryAdd( preparedInstance, placementCandidate.X, placementCandidate.Y, placementCandidate.IsRotated );
		if ( !addResult.Success )
		{
			// Rollback : la source a seulement été décrémentée (amount < Quantity garanti ci-dessus,
			// TryConsume n'a donc jamais pu retirer le placement) — restaurer sa quantité d'origine suffit.
			if ( !sourceContainer.TryGrowQuantity( sourceInstanceId, amount ).Success )
				return StackTransactionResult.Fail( StackTransactionFailureReason.RollbackFailed, sourceInstanceId, null, amount );

			return StackTransactionResult.Fail( StackTransactionFailureReason.UnexpectedMutationFailure, sourceInstanceId, null, amount );
		}

		if ( ValidateAffectedContainers( sourceContainer, targetContainer ) )
			return StackTransactionResult.Ok( sourceInstanceId, null, preparedInstance.InstanceId, amount, amount );

		// Les deux mutations ont réussi mais l'état final viole un invariant interne : rollback complet
		// (retrait de la pile ajoutée, restauration de la quantité source) avant de rapporter l'échec —
		// un Fail ne doit jamais laisser subsister une mutation observable.
		bool rollbackOk = RollbackPartialTransfer( sourceContainer, targetContainer, sourceInstanceId, amount, preparedInstance.InstanceId );
		return FinalizeAfterFailedValidation( rollbackOk, sourceContainer, targetContainer, sourceInstanceId, null, amount );
	}

	/// <summary>
	/// Rollback d'un transfert partiel/split dont l'état final a échoué à <see cref="ValidateAffectedContainers"/>
	/// malgré des mutations individuellement réussies : retire la pile nouvellement ajoutée côté cible,
	/// puis restaure la quantité d'origine côté source. Fonctionne aussi bien entre deux conteneurs
	/// distincts qu'avec <paramref name="sourceContainer"/> == <paramref name="targetContainer"/> (les deux
	/// opérations s'appliquent alors simplement au même objet, sans double traitement).
	/// </summary>
	static bool RollbackPartialTransfer( InventoryContainer sourceContainer, InventoryContainer targetContainer, Guid sourceInstanceId, int amount, Guid newInstanceId )
	{
		if ( !targetContainer.TryRemove( newInstanceId, out _ ).Success )
			return false;

		return sourceContainer.TryGrowQuantity( sourceInstanceId, amount ).Success;
	}

	// --- Merge (un ou deux conteneurs) ---

	/// <summary>
	/// Fusionne exactement <paramref name="amount"/> unités de <paramref name="sourceInstanceId"/>
	/// vers <paramref name="targetInstanceId"/> — tout ou rien, jamais de réduction silencieuse.
	/// Merge total (<c>amount == source.Quantity</c>) si la cible a la place : le placement source
	/// disparaît, la cible conserve son identité. Merge partiel sinon : les deux identités
	/// survivent, seules leurs quantités changent.
	/// </summary>
	public static StackTransactionResult TryMergeExact( InventoryContainer sourceContainer, InventoryContainer targetContainer, Guid sourceInstanceId, Guid targetInstanceId, int amount )
	{
		var failure = ResolveMergePair( sourceContainer, targetContainer, sourceInstanceId, targetInstanceId, out var source, out var target );
		if ( failure != StackTransactionFailureReason.None )
			return StackTransactionResult.Fail( failure, sourceInstanceId, targetInstanceId, amount );

		if ( amount < 1 )
			return StackTransactionResult.Fail( StackTransactionFailureReason.InvalidAmount, sourceInstanceId, targetInstanceId, amount );

		if ( amount > source.Item.Quantity )
			return StackTransactionResult.Fail( StackTransactionFailureReason.InsufficientSourceQuantity, sourceInstanceId, targetInstanceId, amount );

		int remaining = target.Item.Definition.MaxStack - target.Item.Quantity;
		if ( remaining <= 0 )
			return StackTransactionResult.Fail( StackTransactionFailureReason.TargetFull, sourceInstanceId, targetInstanceId, amount );

		if ( amount > remaining )
			return StackTransactionResult.Fail( StackTransactionFailureReason.InsufficientTargetCapacity, sourceInstanceId, targetInstanceId, amount );

		// requestedAmount == amount : pour TryMergeExact, la quantité déplacée est toujours exactement
		// celle demandée par l'appelant (tout ou rien) — voir TryMergeUntilCapacity pour le cas où
		// aucune quantité n'est explicitement demandée.
		return ExecuteMerge( sourceContainer, targetContainer, sourceInstanceId, targetInstanceId, amount, requestedAmount: amount );
	}

	/// <summary>
	/// Fusionne <c>min(source.Quantity, capacité restante de la cible)</c> unités — jamais un
	/// échec pour « pas assez de place », seulement si la cible est déjà pleine
	/// (<see cref="StackTransactionFailureReason.TargetFull"/>, aucune unité déplaçable).
	/// <see cref="StackTransactionResult.MovedAmount"/> porte la quantité réellement déplacée.
	/// Cette opération ne reçoit aucune quantité explicite de l'appelant : il n'existe donc pas de
	/// <c>requestedAmount</c> métier pour elle — <see cref="StackTransactionResult.RequestedAmount"/>
	/// vaut toujours <c>0</c> ici, succès comme échec, jamais la quantité calculée en interne.
	/// </summary>
	public static StackTransactionResult TryMergeUntilCapacity( InventoryContainer sourceContainer, InventoryContainer targetContainer, Guid sourceInstanceId, Guid targetInstanceId )
	{
		var failure = ResolveMergePair( sourceContainer, targetContainer, sourceInstanceId, targetInstanceId, out var source, out var target );
		if ( failure != StackTransactionFailureReason.None )
			return StackTransactionResult.Fail( failure, sourceInstanceId, targetInstanceId, 0 );

		int remaining = target.Item.Definition.MaxStack - target.Item.Quantity;
		if ( remaining <= 0 )
			return StackTransactionResult.Fail( StackTransactionFailureReason.TargetFull, sourceInstanceId, targetInstanceId, 0 );

		int amount = Math.Min( source.Item.Quantity, remaining );
		return ExecuteMerge( sourceContainer, targetContainer, sourceInstanceId, targetInstanceId, amount, requestedAmount: 0 );
	}

	/// <summary>
	/// Ordre des validations : conteneurs valides, source présente, cible présente, identité
	/// (<c>sourceId != targetId</c> dans le même conteneur), source empilable, compatibilité — dans
	/// cet ordre précis, pour qu'une source/cible inexistante soit toujours rapportée comme
	/// <see cref="StackTransactionFailureReason.SourceNotFound"/>/<see cref="StackTransactionFailureReason.TargetNotFound"/>
	/// plutôt que masquée par <see cref="StackTransactionFailureReason.SameSourceAndTarget"/> quand
	/// les deux identifiants coïncident par ailleurs.
	/// </summary>
	static StackTransactionFailureReason ResolveMergePair(
		InventoryContainer sourceContainer, InventoryContainer targetContainer,
		Guid sourceInstanceId, Guid targetInstanceId,
		out InventoryPlacement source, out InventoryPlacement target )
	{
		source = null;
		target = null;

		if ( sourceContainer is null )
			return StackTransactionFailureReason.SourceNotFound;

		if ( targetContainer is null )
			return StackTransactionFailureReason.TargetNotFound;

		source = sourceContainer.GetPlacement( sourceInstanceId );
		if ( source is null )
			return StackTransactionFailureReason.SourceNotFound;

		target = targetContainer.GetPlacement( targetInstanceId );
		if ( target is null )
			return StackTransactionFailureReason.TargetNotFound;

		if ( ReferenceEquals( sourceContainer, targetContainer ) && sourceInstanceId == targetInstanceId )
			return StackTransactionFailureReason.SameSourceAndTarget;

		if ( source.Item.Definition.MaxStack <= 1 )
			return StackTransactionFailureReason.SourceNotStackable;

		if ( !source.Item.CanStackWith( target.Item ) )
			return StackTransactionFailureReason.IncompatibleStacks;

		return StackTransactionFailureReason.None;
	}

	/// <summary>
	/// Mutations (préflight déjà validé par l'appelant) : décrément source
	/// (<see cref="InventoryContainer.TryConsume"/>, qui applique déjà seul la politique
	/// d'identité — retrait du placement si <c>amount == Quantity</c>, décrément en place sinon)
	/// puis croissance cible (<see cref="InventoryContainer.TryGrowQuantity"/>). Un échec inattendu
	/// de la croissance restaure la source exactement comme avant, que la consommation ait été
	/// totale (replacement du placement retiré à sa position d'origine) ou partielle (croissance
	/// inverse) — jamais de mutation observable qui survivrait à un échec. <paramref name="amount"/>
	/// est la quantité réellement exécutée ; <paramref name="requestedAmount"/> est uniquement la
	/// valeur rapportée dans le résultat (identique à <paramref name="amount"/> pour
	/// <see cref="TryMergeExact"/>, toujours <c>0</c> pour <see cref="TryMergeUntilCapacity"/>).
	/// </summary>
	static StackTransactionResult ExecuteMerge( InventoryContainer sourceContainer, InventoryContainer targetContainer, Guid sourceInstanceId, Guid targetInstanceId, int amount, int requestedAmount )
	{
		// État initial validé avant toute mutation — défense en profondeur, voir ValidateAffectedContainers.
		if ( !ValidateAffectedContainers( sourceContainer, targetContainer ) )
			return StackTransactionResult.Fail( StackTransactionFailureReason.InvariantViolation, sourceInstanceId, targetInstanceId, requestedAmount );

		var consumeResult = sourceContainer.TryConsume( sourceInstanceId, amount );
		if ( !consumeResult.Success )
			return StackTransactionResult.Fail( StackTransactionFailureReason.UnexpectedMutationFailure, sourceInstanceId, targetInstanceId, requestedAmount );

		var growResult = targetContainer.TryGrowQuantity( targetInstanceId, amount );
		if ( !growResult.Success )
		{
			bool restored = sourceContainer.Contains( sourceInstanceId )
				? sourceContainer.TryGrowQuantity( sourceInstanceId, amount ).Success
				: sourceContainer.TryAdd( consumeResult.Placement.Item, consumeResult.Placement.X, consumeResult.Placement.Y, consumeResult.Placement.IsRotated ).Success;

			if ( !restored )
				return StackTransactionResult.Fail( StackTransactionFailureReason.RollbackFailed, sourceInstanceId, targetInstanceId, requestedAmount );

			return StackTransactionResult.Fail( StackTransactionFailureReason.UnexpectedMutationFailure, sourceInstanceId, targetInstanceId, requestedAmount );
		}

		if ( ValidateAffectedContainers( sourceContainer, targetContainer ) )
			return StackTransactionResult.Ok( sourceInstanceId, targetInstanceId, null, requestedAmount, amount );

		// Les deux mutations ont réussi mais l'état final viole un invariant interne : rollback complet
		// avant de rapporter l'échec — un Fail ne doit jamais laisser subsister une mutation observable.
		bool rollbackOk = RollbackMerge( sourceContainer, targetContainer, sourceInstanceId, targetInstanceId, amount, consumeResult.Placement );
		return FinalizeAfterFailedValidation( rollbackOk, sourceContainer, targetContainer, sourceInstanceId, targetInstanceId, requestedAmount );
	}

	/// <summary>
	/// Rollback d'un merge dont l'état final a échoué à <see cref="ValidateAffectedContainers"/> malgré
	/// des mutations individuellement réussies : retire de la cible la quantité qu'elle vient de
	/// recevoir (jamais une consommation totale — la cible avait au moins 1 unité avant croissance),
	/// puis restaure la source, soit par croissance (merge partiel, placement toujours présent), soit
	/// en réinsérant <paramref name="originalSourcePlacement"/> à sa position/rotation d'origine
	/// (merge total, placement déjà retiré par <see cref="InventoryContainer.TryConsume"/>).
	/// </summary>
	static bool RollbackMerge( InventoryContainer sourceContainer, InventoryContainer targetContainer, Guid sourceInstanceId, Guid targetInstanceId, int amount, InventoryPlacement originalSourcePlacement )
	{
		if ( !targetContainer.TryConsume( targetInstanceId, amount ).Success )
			return false;

		bool sourceRestored = sourceContainer.Contains( sourceInstanceId )
			? sourceContainer.TryGrowQuantity( sourceInstanceId, amount ).Success
			: sourceContainer.TryAdd( originalSourcePlacement.Item, originalSourcePlacement.X, originalSourcePlacement.Y, originalSourcePlacement.IsRotated ).Success;

		return sourceRestored;
	}

	/// <summary>
	/// Décide de la raison finale à rapporter après une tentative de rollback consécutive à un échec
	/// de validation finale (les deux mutations avaient réussi individuellement) : <see cref="StackTransactionFailureReason.InvariantViolation"/>
	/// uniquement si le rollback a réussi et que l'état restauré revalide correctement — sinon
	/// <see cref="StackTransactionFailureReason.RollbackFailed"/>, jamais un échec ordinaire masquant
	/// une corruption réelle.
	/// </summary>
	static StackTransactionResult FinalizeAfterFailedValidation( bool rollbackSucceeded, InventoryContainer sourceContainer, InventoryContainer targetContainer, Guid sourceInstanceId, Guid? targetInstanceId, int requestedAmount )
	{
		if ( rollbackSucceeded && ValidateAffectedContainers( sourceContainer, targetContainer ) )
			return StackTransactionResult.Fail( StackTransactionFailureReason.InvariantViolation, sourceInstanceId, targetInstanceId, requestedAmount );

		return StackTransactionResult.Fail( StackTransactionFailureReason.RollbackFailed, sourceInstanceId, targetInstanceId, requestedAmount );
	}

	static bool ValidateAffectedContainers( InventoryContainer a, InventoryContainer b )
	{
		if ( !a.TryValidateState( out _ ) )
			return false;

		if ( !ReferenceEquals( a, b ) && !b.TryValidateState( out _ ) )
			return false;

		return true;
	}
}
