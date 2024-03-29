﻿using DomainBlocks.ThirdParty.SqlStreamStore.Infrastructure;
using DomainBlocks.ThirdParty.SqlStreamStore.Imports.Ensure.That;

namespace DomainBlocks.ThirdParty.SqlStreamStore.Streams
{
    /// <summary>
    ///     Represents a valid Stream Id. Is implicitly convertable to/from a string.
    /// </summary>
    public sealed class StreamId : IEquatable<StreamId>
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="StreamId"/> class.
        /// </summary>
        /// <param name="value">The value.</param>
        public StreamId(string value)
        {
            Ensure.That(value, nameof(value))
                .IsNotNullOrWhiteSpace()
                .DoesNotContainWhitespace();
            Value = value;
        }

        /// <summary>
        ///     Gets the value.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Returns a string representation of the current StreamId value.
        /// 
        /// </summary>
        /// 
        /// <returns>
        /// String representation of the StreamId value.
        /// </returns>
        public override string ToString()
        {
            return Value;
        }

        /// <summary>
        ///     Performs an implicit conversion from <see cref="StreamId"/> to <see cref="System.String"/>.
        /// </summary>
        /// <param name="streamId">The stream identifier.</param>
        /// <returns>
        ///     The result of the conversion.
        /// </returns>
        public static implicit operator string(StreamId streamId) => streamId?.Value;

        /// <summary>
        ///     Performs an implicit conversion from <see cref="System.String"/> to <see cref="StreamId"/>.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>
        ///     The result of the conversion.
        /// </returns>
        public static implicit operator StreamId(string value) => new StreamId(value);

        /// <inheritdoc />
        public bool Equals(StreamId other) =>
            !ReferenceEquals(null, other) && (ReferenceEquals(this, other) || string.Equals(Value, other.Value));

        /// <inheritdoc />
        public override bool Equals(object obj) =>
            !ReferenceEquals(null, obj) && (ReferenceEquals(this, obj) || obj is StreamId && Equals((StreamId)obj));

        /// <inheritdoc />
        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(StreamId left, StreamId right) => Equals(left, right);

        public static bool operator !=(StreamId left, StreamId right) => !Equals(left, right);
    }
}