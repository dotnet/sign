// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;

namespace Sign.Core
{
    internal static class ClickOnceInvocationPreflight
    {
        internal static void ValidateSources(
            IReadOnlyList<SigningOperationPlan> plans,
            IEnumerable<SigningFile> sources)
        {
            ILookup<string, SigningFile> inputs = sources.ToLookup(
                source => source.File.FullName, ClickOnceDestinationComparer.Instance);
            foreach (SigningOperationPlan plan in plans)
            {
                foreach (SigningFile source in inputs[plan.Output.FullName])
                {
                    if (!plan.Source.SourceIdentity.Equals(source.SourceIdentity))
                    {
                        throw Conflict(plan.Output, plan.Source, source, source);
                    }
                }
            }
        }

        internal static void ValidatePublications(
            IReadOnlyList<SigningOperationPlan> plans,
            IEnumerable<ClickOncePublicationClaim> publications,
            IReadOnlyDictionary<SigningSourceIdentity, SigningOperationResult> explicitResults)
        {
            ILookup<string, SigningOperationPlan> outputs = plans.ToLookup(
                plan => plan.Output.FullName, ClickOnceDestinationComparer.Instance);
            Dictionary<string, ClickOncePublicationClaim> graphOutputs = new(ClickOnceDestinationComparer.Instance);
            foreach (ClickOncePublicationClaim claim in publications)
            {
                if (graphOutputs.TryGetValue(claim.Destination.FullName, out ClickOncePublicationClaim? existing))
                {
                    if (!existing.Source.SourceIdentity.Equals(claim.Source.SourceIdentity) ||
                        !ReferenceEquals(existing.Result, claim.Result))
                    {
                        throw Conflict(claim.Destination, existing.Publisher, claim.Publisher, claim.Source);
                    }
                }
                else
                {
                    graphOutputs.Add(claim.Destination.FullName, claim);
                }
                foreach (SigningOperationPlan plan in outputs[claim.Destination.FullName])
                {
                    // Unsupported in-place ordinary inputs are no-ops, not
                    // competing publishers of their original unsigned bytes.
                    bool compatibleResult = explicitResults.TryGetValue(
                        plan.Source.SourceIdentity, out SigningOperationResult? result)
                        ? ReferenceEquals(result, claim.Result)
                        : ClickOnceDestinationComparer.Instance.Equals(plan.Source.File.FullName, plan.Output.FullName);
                    if (!plan.Source.SourceIdentity.Equals(claim.Source.SourceIdentity) ||
                        !compatibleResult)
                    {
                        throw Conflict(claim.Destination, plan.Source, claim.Publisher, claim.Source);
                    }
                }
            }
        }

        private static SigningException Conflict(
            FileInfo destination, SigningFile input, SigningFile publisher, SigningFile source) =>
            new(string.Format(CultureInfo.CurrentCulture, Resources.ClickOnceOwnershipCollision,
                destination.FullName, input.File.FullName, publisher.File.FullName, source.File.FullName));
    }
}
