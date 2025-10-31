using UnityEngine;
using UnityEngine.SceneManagement;

public class reloadGame : MonoBehaviour
{
    public void ReloadScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
