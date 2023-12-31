﻿using System.Reflection;
using System.Text;
using NativeAppStore.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NativeAppStore.Core;

public class StoreResolver
{
    private readonly string _encryptionKey; // 加密密钥


    private const BindingFlags Binds = BindingFlags.Public | BindingFlags.Default |
                                       BindingFlags.Instance | BindingFlags.Static;

    // 获取binds 去除了 BindingFlags.NonPublic 的值
    private const BindingFlags BindsWithoutNonPublic = Binds & ~BindingFlags.Public | BindingFlags.NonPublic;


    public StoreResolver(string? encryptionKey = null)
    {
        this._encryptionKey = encryptionKey ?? StoreExtensions.StoreOptions.EncryptionKey;
    }

    public IEnumerable<MemberInfo> GetRuleMembers(Type type)
    {
        var properties = type.GetProperties(Binds).Select(t => t as MemberInfo)
            .Concat(type.GetProperties(BindsWithoutNonPublic).Where(t =>
                    t.GetSingleAttributeOrNull<StoreIgnoreAttribute>() == null &&
                    (t.GetSingleAttributeOrNull<StoreAttribute>() != null ||
                     t.GetSingleAttributeOrNull<StoreEncryptAttribute>() != null))
                .Select(t => t as MemberInfo)).ToList();

        var fields = type.GetFields(Binds).Select(t => t as MemberInfo)
            .Concat(type.GetFields(BindsWithoutNonPublic).Where(t =>
                    t.GetSingleAttributeOrNull<StoreIgnoreAttribute>() == null &&
                    (t.GetSingleAttributeOrNull<StoreAttribute>() != null ||
                     t.GetSingleAttributeOrNull<StoreEncryptAttribute>() != null))
                .Select(t => t as MemberInfo)).ToList();

        List<MemberInfo> members = properties.Concat(fields).ToList();

        return members;
    }


    public object GetMemberValueOnConvertor(MemberInfo member, object? obj, bool isEncryptClass)
    {
        object? value = GetMemberValue(member, obj);
        Type type = GetMemberRealType(member);

        // 检测memberValue 是否是复杂对象类型
        if (value != null && type.IsClass && type != typeof(string))
        {
            value = JsonConvert.SerializeObject(value, new StoreConverter());
        }

        // 检查属性是否有 StoreEncryptionAttribute 特性
        bool encryptProperty = member.GetCustomAttribute(typeof(StoreEncryptAttribute)) != null;
        if (isEncryptClass || encryptProperty)
        {
            if (value is string memberValueString)
            {
                value = Encrypt(memberValueString, _encryptionKey);
            }
            else
            {
                // throw new ArgumentException("StoreEncryptionAttribute can only be used on string properties",
                //     nameof(member));
            }
        }

        return value;
    }

    private Type GetMemberRealType(MemberInfo member)
    {
        var propertyInfo = member as PropertyInfo;
        var fieldInfo = member as FieldInfo;

        return propertyInfo?.PropertyType ?? fieldInfo?.FieldType!;
    }

    public object? GetMemberValue(MemberInfo member, object? obj)
    {
        var propertyInfo = member as PropertyInfo;
        var fieldInfo = member as FieldInfo;

        return propertyInfo?.GetValue(obj) ?? fieldInfo?.GetValue(obj);
    }

    public void SetMemberValueOnConvertor(MemberInfo member, object? obj, JToken jValue, bool isEncryptClass)
    {
        object? value = null;
        Type type = GetMemberRealType(member);


        // 检测memberValue 是否是复杂对象类型
        if (jValue.Value<string?>() != null && type.IsClass && type != typeof(string))
        {
            value = JsonConvert.DeserializeObject(jValue.ToString(), type, new StoreConverter());
        }
        else
        {
            value = jValue.ToObject(type);
        }

        // 检查属性是否有 StoreEncryptionAttribute 特性
        var isEncryptProperty = member.GetCustomAttribute(typeof(StoreEncryptAttribute)) != null;
        if (isEncryptClass || isEncryptProperty)
        {
            if (type == typeof(string))
            {
                value = Decrypt(value?.ToString(), _encryptionKey);
            }
            else
            {
                // throw new ArgumentException("StoreEncryptionAttribute can only be used on string properties",
                //     nameof(member));
            }
        }


        SetMemberValue(member, obj, value);
    }


    public void SetMemberValue(MemberInfo member, object? obj, object? value)
    {
        var propertyInfo = member as PropertyInfo;
        var fieldInfo = member as FieldInfo;

        propertyInfo?.SetValue(obj, value);
        fieldInfo?.SetValue(obj, value);
    }

    private string? Encrypt(string? plainText, string key)
    {
        return plainText == null
            ? null
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
    }

    private string? Decrypt(string? cipherText, string key)
    {
        return cipherText == null
            ? null
            : Encoding.UTF8.GetString(Convert.FromBase64String(cipherText));
    }
}