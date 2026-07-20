using System;
using Kodoku.Player;

namespace Kodoku.World.Containers;

/// <summary>
/// Rend un <see cref="WorldContainerComponent"/> ouvrable via le système de pression stock du moteur
/// (<c>Component.IPressable</c>), même mécanisme déjà validé pour <c>WorldItemPickupComponent</c> —
/// aucun nouveau scanner d'interaction. Composant séparé plutôt qu'implémentation directe de
/// <c>IPressable</c> sur <see cref="WorldContainerComponent"/> (Approche B, voir
/// docs/architecture/WORLD_CONTAINER_ARCHITECTURE.md section 8) : même séparation déjà établie dans
/// ce projet entre <c>WorldItemComponent</c> (état/réseau) et
/// <c>WorldItemPickupComponent</c> (interaction) — un composant fait une chose
/// (.claude/rules/csharp.md). <see cref="WorldContainerComponent"/> porte déjà une responsabilité
/// large (grille canonique, viewers, transferts, transport réseau) ; lui ajouter la détection
/// d'interaction la ferait déborder sur un second domaine (input/monde) pour un bénéfice nul — cette
/// séparation ne coûte qu'un fichier et un <c>[RequireComponent]</c>.
///
/// <see cref="Press"/> ne mute jamais l'état de gameplay directement, et ne duplique aucune logique
/// de validation métier (le host revalide déjà tout — appelant, distance — dans
/// <c>TryOpenAuthoritative</c>). Il se contente de déclencher
/// <see cref="WorldContainerComponent.RequestLocalOpen"/> — pas <c>RequestOpen()</c> directement :
/// <c>RequestLocalOpen()</c> ajoute la coordination locale nécessaire pour garantir l'invariant
/// "un seul conteneur actif dans l'UI par joueur local" (fermeture immédiate de l'ancien conteneur
/// actif s'il diffère de la cible, filtre anti-réponse-retardée).
/// </summary>
public sealed class WorldContainerInteractionComponent : Component, Component.IPressable
{
	/// <summary>
	/// <c>[RequireComponent]</c> résout (ou crée) le <see cref="WorldContainerComponent"/> du même
	/// GameObject — même patron que <c>WorldItemPickupComponent.WorldItem</c>.
	/// </summary>
	[RequireComponent]
	public WorldContainerComponent WorldContainer { get; set; }

	// Component.IPressable — implémentation publique implicite, même convention que
	// WorldItemPickupComponent/BaseChair (stock). Press() est le seul membre sans corps par défaut ;
	// CanPress()/GetTooltip() sont surchargés pour un retour local utile, le reste (Hover/Look/
	// Blur/Pressing/Release) garde son comportement par défaut : ouvrir un conteneur est une action
	// instantanée (déclenche une requête), pas un maintien — le panneau qui s'ouvrira ensuite (hors
	// périmètre de cette étape) est piloté par la réception du snapshot, pas par la pression elle-même.

	public bool CanPress( IPressable.Event e )
	{
		// Vérification locale, indicative uniquement — la seule vérification qui compte réellement se
		// fait côté host dans WorldContainerComponent.TryOpenAuthoritative (autorité, distance, état du
		// GameObject). Couvre uniquement le cas où la cible a disparu localement (ex. déjà détruite)
		// entre la détection stock du regard et l'appui effectif.
		return WorldContainer.IsValid() && WorldContainer.GameObject.IsValid();
	}

	public bool Press( IPressable.Event e )
	{
		WorldContainer.RequestLocalOpen();

		// false : pas de maintien attendu pour une demande d'ouverture (voir Component.IPressable.Press,
		// « If it returns true then you should call Release when the press finishes »). L'ouverture
		// effective (session confirmée) arrive plus tard, de façon asynchrone, via la réception du
		// snapshot (WorldContainerComponent.ReceiveSnapshot / LocalOpenContainer) — jamais synchrone
		// avec cet appel. La fermeture d'un éventuel ancien conteneur actif, elle, est immédiate et
		// synchrone (voir RequestLocalOpen/CloseLocalSessionImmediately).
		return false;
	}

	public IPressable.Tooltip? GetTooltip( IPressable.Event e )
	{
		if ( !WorldContainer.IsValid() || !WorldContainer.GameObject.IsValid() )
			return null;

		// Le tooltip ne doit jamais apparaître à une distance où l'ouverture échouerait de toute façon
		// (WorldContainerComponent.TryOpenAuthoritative rejette au-delà de cette même portée effective) :
		// la détection stock du regard (PlayerController.Hovered/Tooltips) n'est, elle, pas bornée par
		// ReachLength — seul le press l'est. Vérification locale/indicative uniquement, comme CanPress —
		// c'est TryOpenAuthoritative qui revalide réellement côté host.
		if ( !IsWithinTooltipRange() )
			return null;

		return new IPressable.Tooltip( $"Ouvrir {WorldContainer.GameObject.Name}", "", "" );
	}

	bool IsWithinTooltipRange()
	{
		var controller = KodokuPlayerComponent.Local?.PlayerController;
		if ( controller is null )
			return false;

		var effectiveRange = MathF.Min( controller.ReachLength, WorldContainer.MaxOpenDistance );
		return Vector3.DistanceBetween( controller.EyePosition, WorldContainer.GameObject.WorldPosition ) <= effectiveRange;
	}
}
