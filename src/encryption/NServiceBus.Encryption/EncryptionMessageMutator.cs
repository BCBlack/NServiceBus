using System;
using System.Linq;
using Common.Logging;
using NServiceBus.MessageMutator;

namespace NServiceBus.Encryption
{
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Reflection;
    using Config;

    /// <summary>
    /// Invokes the encryption service to encrypt/decrypt messages
    /// </summary>
    public class EncryptionMessageMutator : IMessageMutator
    {
        public IEncryptionService EncryptionService { get; set; }

        public object MutateOutgoing(object message)
        {
            ForEachMember(message, EncryptMember, IsEncryptedMember);

            return message;
        }


        public object MutateIncoming(object message)
        {
            ForEachMember(message, DecryptMember, IsEncryptedMember);
            return message;
        }

        bool IsEncryptedMember(MemberInfo arg)
        {
            if (arg is PropertyInfo)
                return ((PropertyInfo)arg).IsEncryptedProperty();

            if (arg is FieldInfo)
                return ((FieldInfo)arg).FieldType == typeof(WireEncryptedString);

            return false;

        }
        void ForEachMember(object root, Action<object, MemberInfo> action, Func<MemberInfo, bool> appliesTo)
        {
            if (root == null || visitedMembers.Contains(root))
                return;

            visitedMembers.Add(root);

            var members = GetFieldsAndProperties(root);

            foreach (var member in members)
            {

                if (appliesTo(member))
                    action(root, member);

                //don't recurse over primitives and system types
                if (member.ReflectedType.IsPrimitive || member.ReflectedType.IsSystemType())
                    continue;

                var child = member.GetValue(root);

                if (child is IEnumerable)
                    foreach (var item in (IEnumerable)child)
                        ForEachMember(item, action, appliesTo);
                else
                    ForEachMember(child, action, appliesTo);
            }
        }


        void EncryptMember(object target, MemberInfo member)
        {
            var valueToEncrypt = member.GetValue(target);

            if (valueToEncrypt == null)
                return;

            if (EncryptionService == null)
                throw new InvalidOperationException(
                    String.Format("Cannot encrypt field {0} because no encryption service was configured.",
                                  member.Name));

            if (valueToEncrypt is WireEncryptedString)
            {
                var encryptedString = (WireEncryptedString)valueToEncrypt;
                EncryptWireEncryptedString(encryptedString);

                if (!ConfigureEncryption.EnsureCompatibilityWithNSB2)
                {
                    //we clear the properties to avoid having the extra data serialized
                    encryptedString.EncryptedBase64Value = null;
                    encryptedString.Base64Iv = null;
                }
            }
            else
            {
                member.SetValue(target, EncryptUserSpecifiedProperty(valueToEncrypt));
            }

            Log.Debug(member.Name + " encrypted successfully");
        }


        void DecryptMember(object target, MemberInfo property)
        {

            var encryptedValue = property.GetValue(target);

            if (encryptedValue == null)
                return;

            if (EncryptionService == null)
                throw new InvalidOperationException(
                    String.Format("Cannot decrypt field {0} because no encryption service was configured.", property.Name));

            if (encryptedValue is WireEncryptedString)
                Decrypt((WireEncryptedString)encryptedValue);
            else
            {
                property.SetValue(target, DecryptUserSpecifiedProperty(encryptedValue));
            }

            Log.Debug(property.Name + " decrypted successfully");
        }

        string DecryptUserSpecifiedProperty(object encryptedValue)
        {
            var stringToDecrypt = encryptedValue as string;

            if (stringToDecrypt == null)
                throw new InvalidOperationException("Only string properties is supported for convention based encryption, please check your convention");

            var parts = stringToDecrypt.Split(new[] { '@' }, StringSplitOptions.None);

            return EncryptionService.Decrypt(new EncryptedValue
            {
                EncryptedBase64Value = parts[0],
                Base64Iv = parts[1]
            });
        }

        void Decrypt(WireEncryptedString encryptedValue)
        {
            if (encryptedValue.EncryptedValue == null)
                throw new InvalidOperationException("Encrypted property is missing encryption data");

            encryptedValue.Value = EncryptionService.Decrypt(encryptedValue.EncryptedValue);
        }

        string EncryptUserSpecifiedProperty(object valueToEncrypt)
        {
            var stringToEncrypt = valueToEncrypt as string;

            if (stringToEncrypt == null)
                throw new InvalidOperationException("Only string properties is supported for convention based encryption, please check your convention");

            var encryptedValue = EncryptionService.Encrypt(stringToEncrypt);

            return string.Format("{0}@{1}", encryptedValue.EncryptedBase64Value, encryptedValue.Base64Iv);
        }

        void EncryptWireEncryptedString(WireEncryptedString wireEncryptedString)
        {
            wireEncryptedString.EncryptedValue = EncryptionService.Encrypt(wireEncryptedString.Value);
            wireEncryptedString.Value = null;

        }
        static IEnumerable<MemberInfo> GetFieldsAndProperties(object target)
        {
            if (target == null)
                return new List<MemberInfo>();

            var messageType = target.GetType();

            if (!cache.ContainsKey(messageType))
                cache[messageType] = messageType.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                  .Where(m => m is FieldInfo || m is PropertyInfo)
                 .ToList();

            return cache[messageType];
        }

        readonly HashSet<object> visitedMembers = new HashSet<object>();

        readonly static IDictionary<Type, IEnumerable<MemberInfo>> cache = new ConcurrentDictionary<Type, IEnumerable<MemberInfo>>();

        readonly static ILog Log = LogManager.GetLogger(typeof(IEncryptionService));
    }


    public static class TypeExtensions
    {
        public static bool IsSystemType(this Type propertyType)
        {
            var nameOfContainingAssembly = propertyType.Assembly.FullName.ToLower();

            return nameOfContainingAssembly.StartsWith("mscorlib") || nameOfContainingAssembly.StartsWith("system.core");
        }
    }

    public static class MemberInfoExtensions
    {
        public static object GetValue(this MemberInfo member, object source)
        {
            if (member is FieldInfo)
                return ((FieldInfo)member).GetValue(source);

            return ((PropertyInfo)member).GetValue(source, null);
        }

        public static void SetValue(this MemberInfo member, object target, object value)
        {
            if (member is FieldInfo)
                ((FieldInfo)member).SetValue(target, value);
            else
                ((PropertyInfo)member).SetValue(target, value, null);
        }

    }
}