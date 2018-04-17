﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Alex.API.Gui;
using Alex.API.Gui.Elements;
using Alex.API.Gui.Elements.Controls;
using Alex.API.Services;
using Alex.API.Utils;
using Alex.GameStates.Gui.Common;

namespace Alex.GameStates.Gui.MainMenu
{
    public class MultiplayerConnectState : GuiStateBase
    {
        private GuiTextInput _hostnameInput;
        private GuiBeaconButton _connectButton;
        private GuiTextElement _errorMessage;

        public MultiplayerConnectState()
        {
            Title = "Connect to Server";

            Gui.AddChild(_hostnameInput = new GuiTextInput()
            {
                Width = 200,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            Gui.AddChild( _connectButton = new GuiBeaconButton("Connect", OnConnectButtonPressed));
            Gui.AddChild(_errorMessage = new GuiTextElement()
            {
                TextColor = TextColor.Red
            });
        }

        private void OnConnectButtonPressed()
        {

            var hostname = _hostnameInput.Value;

            ushort port = 25565;

            var split = hostname.Split(':');
            if (split.Length == 2)
            {
                if (ushort.TryParse(split[1], out port))
                {
                    QueryServer(split[0], port);
                }
                else
                {
                    SetErrorMessage("Invalid Server Address!");
                }
            }
            else if (split.Length == 1)
            {
                QueryServer(split[0], port);
            }
            else
            {
                SetErrorMessage("Invalid Server Address!");
            }
        }

        private void QueryServer(string address, ushort port)
        {
            SetErrorMessage(null);
            SetConnectingState(true);

            var queryProvider = GetService<IServerQueryProvider>();
            queryProvider.QueryServerAsync(address, port).ContinueWith(ContinuationAction);
        }

        private void SetConnectingState(bool connecting)
        {
            if (connecting)
            {
                _connectButton.Text = "Connecting...";
            }
            else
            {
                _connectButton.Text = "Connect";
            }

            _hostnameInput.Enabled = !connecting;
            _connectButton.Enabled = !connecting;
        }

        private void SetErrorMessage(string error)
        {
            _errorMessage.Text = error;
        }

        private void ContinuationAction(Task<ServerQueryResponse> queryTask)
        {
            var response = queryTask.Result;
            
            if (response.Success)
            {
                Alex.ConnectToServer(response.Status.EndPoint);
            }

            SetConnectingState(false);
        }
    }
}
