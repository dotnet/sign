// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class SigningOperationGroup
    {
        private readonly
            IReadOnlyDictionary<
                SigningOperationPlan,
                SigningOperationScope>
            _scopesByPlan;

        internal SigningOperationGroup(
            IReadOnlyList<SigningOperationPlan> plans,
            SigningOperationScope scope,
            IReadOnlyDictionary<
                SigningOperationPlan,
                SigningOperationScope> scopesByPlan)
        {
            ArgumentNullException.ThrowIfNull(plans, nameof(plans));
            ArgumentNullException.ThrowIfNull(scope, nameof(scope));
            ArgumentNullException.ThrowIfNull(
                scopesByPlan,
                nameof(scopesByPlan));

            if (plans.Count != scopesByPlan.Count ||
                plans.Any(plan => !scopesByPlan.ContainsKey(plan)))
            {
                throw new ArgumentException(
                    message:
                        "Every signing operation plan must have a scope.",
                    paramName: nameof(scopesByPlan));
            }

            Plans = plans;
            Scope = scope;
            _scopesByPlan =
                new Dictionary<
                    SigningOperationPlan,
                    SigningOperationScope>(scopesByPlan);
            PlanScopes = _scopesByPlan.Values.ToArray();
        }

        internal IReadOnlyList<SigningOperationPlan> Plans { get; }
        internal IReadOnlyCollection<SigningOperationScope> PlanScopes
        {
            get;
        }
        internal SigningOperationScope Scope { get; }

        internal SigningOperationScope GetScope(
            SigningOperationPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan, nameof(plan));

            return _scopesByPlan[plan];
        }
    }
}
