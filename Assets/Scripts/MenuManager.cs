using UnityEngine;

public class MenuManager : MonoBehaviour
{
    public static MenuManager Instance;

    [SerializeField] Menu[] menus;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void OpenMenu(string menuName)
    {
        bool found = false;

        for (int i = 0; i < menus.Length; i++)
        {
            if (menus[i] == null)
                continue;

            if (menus[i].menuName == menuName)
            {
                menus[i].Open();
                found = true;
            }
            else if (menus[i].open)
            {
                CloseMenu(menus[i]);
            }
        }

        // Otherwise a typo just closes everything and you stare at a blank screen.
        if (!found)
            Debug.LogError($"No menu called '{menuName}'.", this);
    }

    public void OpenMenu(Menu menu)
    {
        if (menu == null)
            return;

        for (int i = 0; i < menus.Length; i++)
        {
            if (menus[i] != null && menus[i].open)
                CloseMenu(menus[i]);
        }

        menu.Open();
    }

    public void CloseMenu(Menu menu)
    {
        if (menu != null)
            menu.Close();
    }
}
