using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ToggleColorButton : MonoBehaviour
{
    [SerializeField] GraphicalBoard graphicalBoard;
    [SerializeField] GameObject thisOutlineImage;
    [SerializeField] GameObject otherOutlineImage1;
    [SerializeField] GameObject otherOutlineImage2;

    public void Selected(int buttonNumber)
    {
        // update the board manager if we're playing ai or friend. also visualize the selection.
        print(buttonNumber);
        thisOutlineImage.SetActive((buttonNumber == 1));
        otherOutlineImage1.SetActive((buttonNumber == 2));
        otherOutlineImage2.SetActive((buttonNumber == 3));
    }
}
