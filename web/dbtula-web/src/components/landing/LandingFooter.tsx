import React from 'react'
import { Logo } from '../brand/Logo'
import { scrollToHero } from '../../lib/utils'

export const LandingFooter: React.FC = () => (
  <footer className="border-t border-border-soft">
    <div className="mx-auto flex max-w-6xl flex-col items-start justify-between gap-4 px-4 py-8 sm:flex-row sm:items-center">
      <Logo />
      <div className="flex items-center gap-5">
        <button
          onClick={scrollToHero}
          className="rounded-md bg-brand-orange px-4 py-2 text-sm font-medium text-white hover:bg-brand-orange-dark"
        >
          Sign in
        </button>
        <span className="text-sm text-text-muted">© 2026 db-tula · All rights reserved.</span>
      </div>
    </div>
  </footer>
)

export default LandingFooter
