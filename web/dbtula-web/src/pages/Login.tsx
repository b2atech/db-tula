import { GoogleOAuthProvider, GoogleLogin } from '@react-oauth/google';
import { useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';

const GOOGLE_CLIENT_ID = import.meta.env.VITE_GOOGLE_CLIENT_ID ?? '';

export default function Login() {
  const navigate = useNavigate();
  const qc = useQueryClient();

  return (
    <GoogleOAuthProvider clientId={GOOGLE_CLIENT_ID}>
      <div className="min-h-screen flex items-center justify-center bg-gray-50">
        <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-10 w-full max-w-sm text-center">
          <div className="flex items-center justify-center gap-3 mb-2">
            <img src="/logo.svg" alt="db-tula" className="h-12 w-12 rounded-xl shadow" />
            <span className="text-3xl font-bold text-indigo-700">db-tula</span>
          </div>
          <p className="text-gray-500 mb-8 text-sm">Database schema comparison platform</p>
          <GoogleLogin
            onSuccess={async (cred) => {
              if (!cred.credential) return;
              await api.auth.google(cred.credential);
              qc.invalidateQueries({ queryKey: ['me'] });
              navigate('/');
            }}
            onError={() => alert('Google sign-in failed')}
            theme="outline"
            size="large"
            text="signin_with"
          />
          <p className="mt-6 text-xs text-gray-400">
            Sign in with your Google account to continue.<br/>
            First user automatically becomes Admin.
          </p>
        </div>
      </div>
    </GoogleOAuthProvider>
  );
}
