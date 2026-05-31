import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api, tokenStore } from '../api/client';
import type { User } from '../api/client';

export function useAuth() {
  const qc = useQueryClient();
  const { data: user, isLoading, error } = useQuery<User>({
    queryKey: ['me'],
    queryFn: api.auth.me,
    retry: false,
    staleTime: 5 * 60 * 1000,
    enabled: !!tokenStore.get(),   // only fetch if token exists
  });

  const logout = () => {
    tokenStore.clear();
    qc.clear();
    window.location.href = '/login';
  };

  return {
    user,
    isLoading: !!tokenStore.get() && isLoading,
    isAuthenticated: !!user,
    error,
    logout,
  };
}
