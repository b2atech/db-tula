import React from 'react'

const Step: React.FC<{ n: number; title: string; children: React.ReactNode }> = ({ n, title, children }) => (
  <div className="space-y-2">
    <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-brand-orange/15 font-semibold text-brand-orange">
      {n}
    </div>
    <h4 className="font-medium text-text-primary">{title}</h4>
    <p className="text-sm text-text-muted">{children}</p>
  </div>
)

export const HowItWorks: React.FC = () => (
  <section id="how-it-works" className="py-14">
    <div className="mx-auto max-w-4xl px-4 text-center">
      <h2 className="mb-8 text-2xl font-semibold text-text-primary">How it works</h2>
      <div className="grid gap-8 sm:grid-cols-3">
        <Step n={1} title="Connect">Register your databases with read-only credentials.</Step>
        <Step n={2} title="Compare">Define a profile and run it on demand or on a schedule.</Step>
        <Step n={3} title="Review &amp; sync">Triage the drift and apply the generated sync script.</Step>
      </div>
    </div>
  </section>
)

export default HowItWorks
