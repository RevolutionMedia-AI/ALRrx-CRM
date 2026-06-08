const APP_CHOOSER_EMAILS = [
  'cuauhtemoc@revolutionmedia.ai',
  'j.lines@revolutionmedia.ai',
  'kevin.escalante@revolutionmedia.ai',
  'david@revolutionmedia.ai',
];

export function shouldShowAppChooser(email: string | null | undefined): boolean {
  if (!email) return false;
  return APP_CHOOSER_EMAILS.some((e) => e.toLowerCase() === email.toLowerCase());
}
