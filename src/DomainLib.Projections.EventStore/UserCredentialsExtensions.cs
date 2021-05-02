﻿using EventStore.ClientAPI.SystemData;

namespace DomainLib.Projections.EventStore
{
    public static class UserCredentialsExtensions
    {
        public static UserCredentials ToEsUserCredentials(this Common.UserCredentials userCredentials)
        {
            return !string.IsNullOrWhiteSpace(userCredentials.AuthToken)
                       ? new UserCredentials(userCredentials.AuthToken)
                       : new UserCredentials(userCredentials.UserName, userCredentials.Password);
        }
    }
}