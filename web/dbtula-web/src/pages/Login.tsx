import { GoogleOAuthProvider, GoogleLogin } from '@react-oauth/google';
import { useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import LandingHeader from '../components/landing/LandingHeader';
import Hero from '../components/landing/Hero';
import TrustStrip from '../components/landing/TrustStrip';
import Features from '../components/landing/Features';
import HowItWorks from '../components/landing/HowItWorks';
import Security from '../components/landing/Security';
import CtaBand from '../components/landing/CtaBand';
import LandingFooter from '../components/landing/LandingFooter';

const GOOGLE_CLIENT_ID = import.meta.env.VITE_GOOGLE_CLIENT_ID ?? '';

export default function Login() {
  const navigate = useNavigate();
  const qc = useQueryClient();

  const signIn = (
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
  );

  return (
    <GoogleOAuthProvider clientId={GOOGLE_CLIENT_ID}>
      <div className="min-h-screen bg-bg-main text-text-primary">
        <LandingHeader />
        <main>
          <Hero signIn={signIn} />
          <TrustStrip />
          <Features />
          <HowItWorks />
          <Security />
          <CtaBand />
        </main>
        <LandingFooter />
      </div>
    </GoogleOAuthProvider>
  );
}
