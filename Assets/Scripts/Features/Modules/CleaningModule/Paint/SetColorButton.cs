using System;
using UnityEngine;
using UnityEngine.UI;

namespace Paint
{
    public class SetColorButton:MonoBehaviour
    {
        private Color _color;
        private Button _button; 
        [SerializeField] private MousePainter painter;
        [SerializeField] private PlayerPainter playerPainter;



        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(Apply);
            _color = _button.GetComponent<Image>().color;
        }

        private void OnDestroy()
        {
            _button.onClick.RemoveListener(Apply);
        }

        private void Apply()
        {
            painter.SetColor(_color);
            playerPainter.SetColor(_color);
        }
    }
}