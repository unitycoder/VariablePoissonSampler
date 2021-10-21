﻿using System.Collections.Generic;

namespace VertexFragment
{
    public static class ListExtensions
    {
        public static void RemoveUnorderedAt<T>(this List<T> list, int index)
        {
            list[index] = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
        }
    }
}
