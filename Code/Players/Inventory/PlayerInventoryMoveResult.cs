namespace Kodoku.Player.Inventory;

/// <summary>
/// Résultat explicite d'une tentative de déplacement interne dans la grille canonique du pawn —
/// jamais un simple booléen, même patron que <see cref="EquipmentOperationResult"/>/<see cref="DropResult"/>.
/// Transporté au seul propriétaire du pawn (<c>[Rpc.Owner]</c>, voir
/// <see cref="PlayerInventoryComponent.ReceiveMoveResult"/>), jamais un second canal d'état canonique :
/// le contenu réel voyage uniquement par <see cref="InventorySnapshotEntry"/>.
/// </summary>
public readonly record struct PlayerInventoryMoveResult
{
	public bool Success { get; }

	/// <summary>Identifiant de corrélation de la requête cliente à l'origine de ce résultat (voir <see cref="PlayerInventoryComponent.RequestMoveItem"/>) — jamais généré côté host, seulement reflété tel que reçu.</summary>
	public string RequestId { get; }

	/// <summary>Représentation chaîne canonique (<c>Guid.ToString()</c>) — même convention que <see cref="InventorySnapshotEntry.InstanceId"/>.</summary>
	public string InstanceId { get; }

	public PlayerInventoryMoveFailureReason FailureReason { get; }

	PlayerInventoryMoveResult( bool success, string requestId, string instanceId, PlayerInventoryMoveFailureReason failureReason )
	{
		Success = success;
		RequestId = requestId;
		InstanceId = instanceId;
		FailureReason = failureReason;
	}

	public static PlayerInventoryMoveResult Ok( string requestId, string instanceId ) => new( true, requestId, instanceId, PlayerInventoryMoveFailureReason.None );

	public static PlayerInventoryMoveResult Fail( string requestId, string instanceId, PlayerInventoryMoveFailureReason failureReason ) => new( false, requestId, instanceId, failureReason );
}
