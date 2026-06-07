import React from 'react'
import { scrollToHero } from '../../lib/utils'

export const CtaBand: React.FC = () => (
  <section className="py-14">
    <div className="mx-auto max-w-6xl px-4">
      <div className="flex flex-col items-start justify-between gap-4 rounded-2xl border border-border-soft bg-bg-card px-8 py-8 sm:flex-row sm:items-center">
        <div>
          <h3 className="text-lg font-semibold text-text-primary">Sign in to start comparing schemas</h3>
          <p className="text-sm text-text-secondary">Catch drift early and keep every environment in sync.</p>
        </div>
        <button
          onClick={scrollToHero}
          className="inline-flex items-center rounded-md bg-brand-orange px-5 py-2.5 text-sm font-medium text-white hover:bg-brand-orange-dark"
        >
          Sign in
        </button>
      </div>
    </div>
  </section>
)

export default CtaBand
