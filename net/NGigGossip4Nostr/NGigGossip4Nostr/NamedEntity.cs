﻿using System;
namespace NGigGossip4Nostr;

public abstract class NamedEntity
{
    private static readonly Dictionary<string, NamedEntity> ENTITY_BY_NAME = new Dictionary<string, NamedEntity>();

    public static List<string> GetAllNames()
    {
        lock (ENTITY_BY_NAME)
        {
            return ENTITY_BY_NAME.Keys.ToList();
        }
    }

    public string Name;
    public NamedEntity(string name)
    {
        this.Name = name;
        lock (ENTITY_BY_NAME)
            if (ENTITY_BY_NAME.ContainsKey(name))
                throw new InvalidOperationException("duplicate name");
            ENTITY_BY_NAME[name] = this;
    }

    public static NamedEntity GetByEntityName(string name)
    {
        lock (ENTITY_BY_NAME)
        {
            if (ENTITY_BY_NAME.ContainsKey(name))
                return ENTITY_BY_NAME[name];
            throw new ArgumentException("Entity not found");
        }
    }
}
