using UnityEngine;
using UnityEngine.UI;

namespace Omnisense
{
    public class CombatManager : MonoBehaviour
    {
        [Header("Player UI")]
        public Text playerNameText;
        public Image playerHPBarImage;

        [Header("Enemy UI")]
        public Text enemyNameText;
        public Image enemyHPBarImage;

        [Header("Action Choices")]
        public Text attackChoiceText;
        public Text defendChoiceText;
        public Text skillChoiceText;
        public Text itemChoiceText;

        private void Start()
        {
            // Set initial test values to show it's working
            if (playerNameText != null) playerNameText.text = "Pikachu";
            if (playerHPBarImage != null) playerHPBarImage.fillAmount = 1.0f;

            if (enemyNameText != null) enemyNameText.text = "Charizard";
            if (enemyHPBarImage != null) enemyHPBarImage.fillAmount = 0.8f;

            if (attackChoiceText != null) attackChoiceText.text = "ATTACK";
            if (defendChoiceText != null) defendChoiceText.text = "DEFEND";
            if (skillChoiceText != null) skillChoiceText.text = "SKILL";
            if (itemChoiceText != null) itemChoiceText.text = "ITEM";
        }
    }
}
