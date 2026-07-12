using System;
using Kodoku.Player.Vitals;

namespace Kodoku.Player;

/// <summary>
/// Point d'entrée principal vers un pawn Kodoku : identifie le pawn local,
/// distingue les proxies distants et expose le <see cref="Sandbox.PlayerController"/> associé.
/// Ne porte aucun état de gameplay (inventaire, santé, etc.) — voir docs/architecture/PLAYER_ARCHITECTURE.md.
/// </summary>
public sealed class KodokuPlayerComponent : Component, IGameObjectNetworkEvents
{
	/// <summary>
	/// Le pawn Kodoku possédé par cette connexion locale, ou null si aucun n'est actuellement contrôlé.
	/// Ne jamais assigné par un proxy distant — voir <see cref="StartControl"/>/<see cref="StopControl"/>.
	/// </summary>
	public static KodokuPlayerComponent Local { get; private set; }

	public static event Action<KodokuPlayerComponent> LocalPlayerAvailable;
	public static event Action<KodokuPlayerComponent> LocalPlayerRemoved;

	/// <summary>
	/// Vrai si ce pawn est simulé par cette connexion (ownership réseau réel), faux pour un proxy distant.
	/// </summary>
	public bool IsLocalPlayer => !IsProxy;

	public PlayerController PlayerController { get; private set; }

	public PlayerVitalsComponent PlayerVitals { get; private set; }

	protected override void OnStart()
	{
		// Résolu ici plutôt qu'en OnAwake : OnAwake d'un composant peut s'exécuter avant que
		// les composants suivants du même GameObject (ex. PlayerVitalsComponent, listé après
		// dans le prefab) soient pleinement prêts. OnStart s'exécute après l'OnAwake de tous
		// les composants du GameObject — résolution de références sœurs fiable à ce stade.
		PlayerController = Components.Get<PlayerController>();
		PlayerVitals = Components.Get<PlayerVitalsComponent>();

		if ( IsProxy )
		{
			Log.Info( $"[KodokuPlayerComponent] Remote proxy registered: {GameObject.Name}" );
			return;
		}

		// Cas confirmé par test à deux instances : le pawn du host est possédé par le host dès sa création,
		// donc il n'y a jamais de transition proxy -> contrôlé et StartControl ne se déclenche pas pour lui.
		// OnStart couvre ce cas ; StartControl reste nécessaire pour une prise de contrôle après coup
		// (reconnexion, transfert d'ownership). Le garde Local == this partagé évite un double déclenchement.
		RegisterLocal();
	}

	// IGameObjectNetworkEvents — déclenché uniquement sur le GameObject dont l'état de contrôle change,
	// jamais reçu par un proxy distant. Couvre les transitions de contrôle après le premier frame ;
	// voir OnStart pour le cas du pawn contrôlé dès sa création (ex. pawn du host).
	void IGameObjectNetworkEvents.StartControl() => RegisterLocal();

	void RegisterLocal()
	{
		if ( Local == this )
			return;

		Local = this;
		Log.Info( $"[KodokuPlayerComponent] Local player available: {GameObject.Name}" );
		LocalPlayerAvailable?.Invoke( this );
	}

	void IGameObjectNetworkEvents.StopControl()
	{
		if ( Local != this )
			return;

		Local = null;
		Log.Info( $"[KodokuPlayerComponent] Local player removed: {GameObject.Name}" );
		LocalPlayerRemoved?.Invoke( this );
	}

	protected override void OnDestroy()
	{
		if ( Local != this )
			return;

		Local = null;
		Log.Info( $"[KodokuPlayerComponent] Local player removed: {GameObject.Name}" );
		LocalPlayerRemoved?.Invoke( this );
	}
}
