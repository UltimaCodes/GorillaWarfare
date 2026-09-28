using Photon.Pun;
using UnityEngine;

/// <summary>
/// The world behind the menu, one copy per map - and in a lobby, it's the map the lobby picked.
///
/// "When I change the map of a lobby, make the background change maps to that map." Each map's
/// copy sits under this object with two markers of its own: where the menu camera stands and where
/// the gorilla stands. Move either marker in the scene to recompose that map's shot. Only the
/// shown map's copy is active, so the others cost nothing; outside a room it's the first map.
/// </summary>
public class MenuBackdrop : MonoBehaviour
{
    [System.Serializable]
    public class View
    {
        [Tooltip("The map this copy is - a key from Resources/Maps.asset.")]
        public string mapKey;

        [Tooltip("The copied map: its geometry, sun and grass. Shown only while it's the pick.")]
        public GameObject world;

        [Tooltip("Where the menu camera stands and looks while this map is shown.")]
        public Transform cameraSpot;

        [Tooltip("Where the menu gorilla stands while this map is shown.")]
        public Transform gorillaSpot;
    }

    [SerializeField] View[] views = new View[0];
    [SerializeField] MenuBackdropCamera menuCamera;
    [SerializeField] Transform gorilla;

    string showing;

    void Start() => Show(Wanted());

    void Update()
    {
        string wanted = Wanted();

        if (wanted != showing)
            Show(wanted);
    }

    static string Wanted() => PhotonNetwork.InRoom ? MapRegistry.Current.key : MapRegistry.Default.key;

    void Show(string key)
    {
        showing = key;

        View view = null;
        foreach (View candidate in views)
        {
            if (candidate != null && candidate.mapKey == key)
                view = candidate;
        }

        // A map with no copy here keeps whichever was up - better than an empty sky.
        if (view == null || view.world == null)
            return;

        foreach (View other in views)
        {
            if (other != null && other.world != null)
                other.world.SetActive(other == view);
        }

        // The sun belongs to whichever copy is showing.
        Light sun = view.world.GetComponentInChildren<Light>();
        if (sun != null)
            RenderSettings.sun = sun;

        if (menuCamera != null && view.cameraSpot != null)
            menuCamera.MoveTo(view.cameraSpot.position, view.cameraSpot.rotation);

        if (gorilla != null && view.gorillaSpot != null)
            gorilla.SetPositionAndRotation(view.gorillaSpot.position, view.gorillaSpot.rotation);
    }
}
