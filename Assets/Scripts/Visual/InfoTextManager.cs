using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class InfoTextManager : MonoBehaviour
{
    [SerializeField] public TextMeshProUGUI openingText;
    [SerializeField] public TextMeshProUGUI depthText;

    public static InfoTextManager Instance { get; private set; }

    private void Awake()
    {
        // If there is already an instance and it is not this one, destroy this object
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }
}
