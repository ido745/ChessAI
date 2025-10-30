using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class toggleFriendAI : MonoBehaviour
{
    [SerializeField] GraphicalBoard graphicalBoard;
    [SerializeField] Image thisToggleImage;
    [SerializeField] Image otherToggleImage;
    [SerializeField] bool isAI;

    public void Selected()
    {
        // update the board manager if we're playing ai or friend. also visualize the selection.
        thisToggleImage.color = new Color(0, 0, 0);
        otherToggleImage.color = new Color(1, 1, 1);

        graphicalBoard.playingAI = isAI;
    }
}
