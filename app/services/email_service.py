"""
Email service for sending invitations via Resend
"""
import os
import logging
from typing import Optional
import resend
from app.config import settings

logger = logging.getLogger(__name__)

# Initialize Resend with API key
resend.api_key = settings.RESEND_API_KEY


def send_invite_email(email: str, invite_code: str, invite_url: str) -> bool:
    """
    Sends an invitation email to the specified email address.
    
    Args:
        email: Target email address
        invite_code: The unique invitation code (UUID)
        invite_url: The full invite URL (not used in email, kept for compatibility)
        
    Returns:
        bool: True if email was sent successfully, False otherwise
    """
    if not settings.RESEND_API_KEY:
        logger.warning("RESEND_API_KEY not configured. Email not sent.")
        return False
    
    try:
        # HTML email template - Code only, no clickable link
        html_content = f"""
    <!DOCTYPE html>
    <html>
    <head>
        <style>
            body {{
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
                line-height: 1.6;
                color: #333;
                max-width: 600px;
                margin: 0 auto;
                padding: 20px;
            }}
            .container {{
                background-color: #f9f9f9;
                border-radius: 8px;
                padding: 30px;
                border: 1px solid #e0e0e0;
            }}
            .header {{
                text-align: center;
                margin-bottom: 30px;
            }}
            .header h1 {{
                color: #2c5282;
                margin: 0;
            }}
            .content {{
                background-color: white;
                padding: 25px;
                border-radius: 6px;
                margin-bottom: 20px;
            }}
            .invite-code {{
                background-color: #edf2f7;
                border: 2px solid #2c5282;
                border-radius: 4px;
                padding: 15px;
                text-align: center;
                margin: 20px 0;
            }}
            .code {{
                font-size: 24px;
                font-weight: bold;
                color: #2c5282;
                letter-spacing: 2px;
                font-family: monospace;
            }}
            .instructions {{
                background-color: #f7fafc;
                padding: 15px;
                border-left: 4px solid #4299e1;
                margin: 20px 0;
            }}
            .footer {{
                text-align: center;
                color: #718096;
                font-size: 14px;
                margin-top: 30px;
            }}
            ol {{
                margin: 10px 0;
                padding-left: 20px;
            }}
            li {{
                margin: 5px 0;
            }}
        </style>
    </head>
    <body>
        <div class="container">
            <div class="header">
                <h1>Welcome to CFC Animal Feed Software</h1>
            </div>
            
            <div class="content">
                <p>Hello!</p>
                
                <p>You've been invited to join the CFC Animal Feed Software platform. We're excited to have you on board!</p>
                
                <div class="invite-code">
                    <p style="margin: 0 0 10px 0; font-size: 14px; color: #4a5568;">Your Invitation Code:</p>
                    <div class="code">{invite_code}</div>
                </div>
                
                <div class="instructions">
                    <p><strong>How to get started:</strong></p>
                    <ol>
                        <li>Go to the registration page</li>
                        <li>Enter the invitation code shown above</li>
                        <li>Complete your profile information</li>
                        <li>Start using the platform!</li>
                    </ol>
                </div>
                
                <p><strong>Important:</strong> This invitation code can only be used once. Please keep it secure until you complete your registration.</p>
                
                <p>If you have any questions or need assistance, please don't hesitate to contact our support team.</p>
            </div>
            
            <div class="footer">
                <p>© 2026 CFC Animal Feed Software. All rights reserved.</p>
                <p>This is an automated message. Please do not reply to this email.</p>
            </div>
        </div>
    </body>
    </html>
    """
        
        # Send the email using Resend
        params = {
            "from": f"CFC Animal Feed <{os.getenv('RESEND_FROM_EMAIL', 'noreply@yourdomain.com')}>",
            "to": [email],
            "subject": "You're invited to CFC Animal Feed Software",
            "html": html_content,
        }
        
        response = resend.Emails.send(params)
        return response.get("id") if response else False
        
    except Exception as e:
        logger.error(f"Error sending email via Resend: {e}")
        return False
