using System;
using System.Collections.Generic;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// 현재 실행 세션의 스토리 진행 flag를 중복 없이 보관한다.
    /// 이벤트의 의미를 해석하지 않고 flag 추가·삭제·전체 교체만 담당한다.
    /// </summary>
    public sealed class StoryFlagSet
    {
        private readonly HashSet<string> flags = new(StringComparer.Ordinal);

        /// <summary>현재 활성화된 flag를 읽기 전용 열거로 제공한다.</summary>
        public IReadOnlyCollection<string> Flags => flags;

        /// <summary>flag 목록이 실제로 변경됐을 때 발생한다.</summary>
        public event Action Changed;

        /// <summary>지정한 flag가 활성화되어 있는지 반환한다.</summary>
        public bool Contains(string flagId)
        {
            return !string.IsNullOrWhiteSpace(flagId) && flags.Contains(flagId);
        }

        /// <summary>유효한 새 flag를 추가하고 변경 여부를 반환한다.</summary>
        public bool Add(string flagId)
        {
            if (string.IsNullOrWhiteSpace(flagId) || !flags.Add(flagId))
            {
                return false;
            }

            Changed?.Invoke();
            return true;
        }

        /// <summary>지정한 flag를 제거하고 변경 여부를 반환한다.</summary>
        public bool Remove(string flagId)
        {
            if (!flags.Remove(flagId))
            {
                return false;
            }

            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 체크포인트 기준 목록으로 현재 flag를 통째로 교체한다.
        /// 복원 시에는 기존 진행 상태가 남지 않도록 한 번의 변경으로 처리한다.
        /// </summary>
        public void ReplaceWith(IEnumerable<string> flagIds)
        {
            HashSet<string> replacement = new(StringComparer.Ordinal);
            if (flagIds != null)
            {
                foreach (string flagId in flagIds)
                {
                    if (!string.IsNullOrWhiteSpace(flagId))
                    {
                        replacement.Add(flagId);
                    }
                }
            }

            if (flags.SetEquals(replacement))
            {
                return;
            }

            flags.Clear();
            flags.UnionWith(replacement);
            Changed?.Invoke();
        }
    }
}
