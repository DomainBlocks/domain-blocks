#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace DomainBlocks.ThirdParty.SqlStreamStore.Imports.Ensure.That
{
    public class TypeParam : Param
    {
        public readonly Type Type;

        public TypeParam(string name, Type type)
            : base(name)
        {
            Type = type;
        }
    }
}