import React from 'react'
import { Logo } from '../brand/Logo'
import { scrollToHero } from '../../lib/utils'

export const LandingHeader: React.FC = () => {
  const handleSignIn = (e: React.MouseEvent) => {
    e.preventDefault()
    scrollToHero()
  }

  return (
    <header className="sticky top-0 z-40 border-b border-border-soft bg-bg-main/80 py-4 backdrop-blur-sm">
      <div className="mx-auto flex max-w-6xl items-center justify-between px-4">
        <Logo />
        <nav className="hidden items-center gap-6 text-sm md:flex">
          <a href="#features" className="text-text-secondary hover:text-text-primary">Features</a>
          <a href="#how-it-works" className="text-text-secondary hover:text-text-primary">How it works</a>
          <a href="#security" className="text-text-secondary hover:text-text-primary">Security</a>
          <button
            onClick={handleSignIn}
            className="ml-2 inline-flex items-center rounded-md bg-brand-orange px-4 py-2 text-sm font-medium text-white hover:bg-brand-orange-dark"
          >
            Sign in
          </button>
        </nav>
        <button
          onClick={handleSignIn}
          className="inline-flex items-center rounded-md bg-brand-orange px-3 py-1.5 text-sm font-medium text-white hover:bg-brand-orange-dark md:hidden"
        >
          Sign in
        </button>
      </div>
    </header>
  )
}

export default LandingHeader
