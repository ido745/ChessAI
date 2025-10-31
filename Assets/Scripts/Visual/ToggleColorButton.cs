using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ToggleColorButton : MonoBehaviour
{
    [SerializeField] GraphicalBoard graphicalBoard;
    [SerializeField] GameObject BlackColorImage;
    [SerializeField] GameObject RandomColorImage;
    [SerializeField] GameObject WhiteColorImage;

    [SerializeField] GameObject WhiteButton;

    public void Selected(int buttonNumber)
    {
        // update the board manager if we're playing ai or friend. also visualize the selection.
        BlackColorImage.SetActive((buttonNumber == 1));   // Playing as black
        RandomColorImage.SetActive((buttonNumber == 2)); // Random
        WhiteColorImage.SetActive((buttonNumber == 3)); // Playing as white

        switch (buttonNumber)
        {
            case 1:
                graphicalBoard.playingColor = 1;
                break;
            case 2:
                graphicalBoard.playingColor = Random.Range(0, 2); ;
                break;
            case 3:
                graphicalBoard.playingColor = 0;
                break;
            default:
                graphicalBoard.playingColor = 0;
                break;
        }
    }

    public void SetColorWhtie()
    {
        RectTransform rt = WhiteButton.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(0, 0);

        RandomColorImage.transform.parent.gameObject.SetActive(false);
        BlackColorImage.transform.parent.gameObject.SetActive(false);

        // Select the white image
        WhiteButton.GetComponentInChildren<Button>().Select();
        WhiteColorImage.SetActive(true);
        BlackColorImage.SetActive(false);
        RandomColorImage.SetActive(false);
    }

    public void EnableAll()
    {
        RectTransform rt = WhiteButton.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(-50, 0);

        RandomColorImage.transform.parent.gameObject.SetActive(true);
        BlackColorImage.transform.parent.gameObject.SetActive(true);
    }
}
