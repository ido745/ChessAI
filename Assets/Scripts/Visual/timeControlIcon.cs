using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class timeControlIcon : MonoBehaviour
{
    [SerializeField] private GameObject rapidImage;
    [SerializeField] private GameObject blitzImage;
    [SerializeField] private GameObject noTimeImage;

    public void changeIcon()
    {
        TMP_Dropdown dropdown = GetComponent<TMP_Dropdown>();
        int index = dropdown.value;

        rapidImage.SetActive(false);
        blitzImage.SetActive(false);
        noTimeImage.SetActive(false);

        switch (index)
        {
            case 0:
                rapidImage.SetActive(true);
                break;
            case 1:
                blitzImage.SetActive(true);
                break;
            case 2:
                noTimeImage.SetActive(true);
                break;
            default:
                break;
        }
    }
}
