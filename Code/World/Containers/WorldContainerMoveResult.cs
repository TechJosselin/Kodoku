namespace Kodoku.World.Containers;

/// <summary>
/// Résultat explicite d'une tentative de déplacement interne — jamais un simple booléen, même patron
/// que <see cref="WorldContainerTransferResult"/>. Transporté au seul appelant à l'origine de la
/// requête, jamais un second canal d'état canonique : le contenu réel voyage uniquement par
/// <see cref="WorldContainerSnapshotEntry"/> (voir <see cref="WorldContainerComponent.ReceiveSnapshot"/>).
/// </summary>
public readonly record struct WorldContainerMoveResult
{
	public bool Success { get; }

	/// <summary>Identifiant de corrélation de la requête cliente à l'origine de ce résultat (voir <see cref="WorldContainerComponent.RequestMoveItem"/>) — jamais généré côté host, seulement reflété tel que reçu.</summary>
	public string RequestId { get; }

	/// <summary>Représentation chaîne canonique (<c>Guid.ToString()</c>) — même convention que <see cref="WorldContainerSnapshotEntry.InstanceId"/>.</summary>
	public string InstanceId { get; }

	public WorldContainerMoveFailureReason FailureReason { get; }

	WorldContainerMoveResult( bool success, string requestId, string instanceId, WorldContainerMoveFailureReason failureReason )
	{
		Success = success;
		RequestId = requestId;
		InstanceId = instanceId;
		FailureReason = failureReason;
	}

	public static WorldContainerMoveResult Ok( string requestId, string instanceId ) => new( true, requestId, instanceId, WorldContainerMoveFailureReason.None );

	public static WorldContainerMoveResult Fail( string requestId, string instanceId, WorldContainerMoveFailureReason failureReason ) => new( false, requestId, instanceId, failureReason );
}
