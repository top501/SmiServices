﻿using System;
using JetBrains.Annotations;
using MongoDB.Bson.Serialization.Attributes;
using Smi.Common.Helpers;


namespace Microservices.CohortPackager.Execution.ExtractJobStorage.MongoDB.ObjectModel
{
    public class MongoCompletedExtractJobDoc : MongoExtractJobDoc, IEquatable<MongoCompletedExtractJobDoc>
    {
        [BsonElement("completedAt")]
        public DateTime CompletedAt { get; set; }

        public MongoCompletedExtractJobDoc(
            [NotNull] MongoExtractJobDoc extractJobDoc,
            [NotNull] DateTimeProvider provider
        ) : base(extractJobDoc)
        {
            JobStatus = ExtractJobStatus.Completed;
            CompletedAt = provider.UtcNow();
        }

        #region Equality Members

        public bool Equals(MongoCompletedExtractJobDoc other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return base.Equals(other) && CompletedAt.Equals(other.CompletedAt);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((MongoCompletedExtractJobDoc)obj);
        }

        public static bool operator ==(MongoCompletedExtractJobDoc left, MongoCompletedExtractJobDoc right) => Equals(left, right);

        public static bool operator !=(MongoCompletedExtractJobDoc left, MongoCompletedExtractJobDoc right) => !Equals(left, right);

        public override int GetHashCode()
        {
            unchecked
            {
                return (base.GetHashCode() * 397) ^ CompletedAt.GetHashCode();
            }
        }

        #endregion
    }
}
