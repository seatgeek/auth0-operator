﻿using System;

namespace Alethic.Auth0.Operator
{

    /// <summary>
    /// Represents an exception that indicates a retry can be attempted at a later time.
    /// </summary>
    public class RetryException : Exception
    {

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the retry</param>
        public RetryException(string? message) :
            base(message)
        {

        }

    }

}
