using System;

namespace Kodoku.Items.Inventory;

/// <summary>
/// Résultat explicite d'une opération de <see cref="InventoryStackTransactions"/> — même
/// principe que <see cref="InventoryOperationResult"/>, jamais un simple booléen. Ne porte
/// aucune donnée réseau (RequestId, révision, Connection, GameObject, viewer, snapshot) : ces
/// responsabilités appartiennent exclusivement à la future couche réseau qui appellera cette
/// transaction, jamais à cette couche pure. Voir docs/architecture/INVENTORY_STACK_ARCHITECTURE.md.
/// </summary>
public readonly record struct StackTransactionResult
{
	public bool Success { get; }

	public StackTransactionFailureReason FailureReason { get; }

	/// <summary>
	/// Raison spatiale sous-jacente quand <see cref="FailureReason"/> vaut
	/// <see cref="StackTransactionFailureReason.DestinationInvalid"/> —
	/// <see cref="InventoryFailureReason.None"/> dans tous les autres cas.
	/// </summary>
	public InventoryFailureReason SpatialFailureReason { get; }

	/// <summary>
	/// Quantité explicitement demandée par l'appelant. Égale à <see cref="MovedAmount"/> en cas de
	/// succès pour toute opération à quantité exacte (split, merge exact, transfert partiel).
	/// Toujours <c>0</c>, succès comme échec, pour <see cref="InventoryStackTransactions.TryMergeUntilCapacity"/>
	/// — cette opération ne reçoit aucune quantité explicite de l'appelant, il n'existe donc pas de
	/// valeur métier à rapporter ici ; lire <see cref="MovedAmount"/> pour connaître la quantité
	/// réellement déplacée par cette opération.
	/// </summary>
	public int RequestedAmount { get; }

	/// <summary>
	/// Quantité réellement déplacée. Égale à <see cref="RequestedAmount"/> en cas de succès pour
	/// toute opération à quantité exacte (split, merge exact, transfert partiel) ; sans relation
	/// avec <see cref="RequestedAmount"/> (toujours <c>0</c>) pour <see cref="InventoryStackTransactions.TryMergeUntilCapacity"/>,
	/// où seul ce champ porte la quantité calculée. Toujours 0 en cas d'échec.
	/// </summary>
	public int MovedAmount { get; }

	public Guid SourceInstanceId { get; }

	/// <summary>Absent pour un split/transfert vers une case vide (pas de cible existante).</summary>
	public Guid? TargetInstanceId { get; }

	/// <summary>Présent uniquement pour un split ou un transfert partiel vers une case vide.</summary>
	public Guid? NewInstanceId { get; }

	StackTransactionResult( bool success, StackTransactionFailureReason failureReason, InventoryFailureReason spatialFailureReason,
		int requestedAmount, int movedAmount, Guid sourceInstanceId, Guid? targetInstanceId, Guid? newInstanceId )
	{
		Success = success;
		FailureReason = failureReason;
		SpatialFailureReason = spatialFailureReason;
		RequestedAmount = requestedAmount;
		MovedAmount = movedAmount;
		SourceInstanceId = sourceInstanceId;
		TargetInstanceId = targetInstanceId;
		NewInstanceId = newInstanceId;
	}

	public static StackTransactionResult Ok( Guid sourceInstanceId, Guid? targetInstanceId, Guid? newInstanceId, int requestedAmount, int movedAmount )
		=> new( true, StackTransactionFailureReason.None, InventoryFailureReason.None, requestedAmount, movedAmount, sourceInstanceId, targetInstanceId, newInstanceId );

	/// <summary>
	/// Construit un résultat d'échec. <paramref name="reason"/> doit être une raison réelle —
	/// appeler cette factory avec <see cref="StackTransactionFailureReason.None"/> serait une
	/// erreur de programmation (un échec sans raison n'a pas de sens), pas un échec métier
	/// ordinaire ; elle lève donc une exception plutôt que de construire silencieusement un
	/// résultat incohérent.
	/// </summary>
	public static StackTransactionResult Fail( StackTransactionFailureReason reason, Guid sourceInstanceId, Guid? targetInstanceId, int requestedAmount,
		InventoryFailureReason spatialFailureReason = InventoryFailureReason.None )
	{
		if ( reason == StackTransactionFailureReason.None )
			throw new ArgumentException( "A failure result cannot use FailureReason.None.", nameof( reason ) );

		return new( false, reason, spatialFailureReason, requestedAmount, 0, sourceInstanceId, targetInstanceId, null );
	}
}
