# UIAutomation Server — AWS Deployment

## Option A: AWS Console (Web UI)

### 1. Launch an EC2 instance

Go to [EC2 > Launch Instance](https://console.aws.amazon.com/ec2/home#LaunchInstances) and set:

| Setting | Value |
|---------|-------|
| AMI | Amazon Linux 2023 |
| Instance type | `t3.small` (~$15/mo) or `t3.micro` (free tier) |
| Storage | 50 GB gp3 |
| Security group | Allow TCP **22** (SSH) and **80** (HTTP) |
| Key pair | Create or select one |

### 2. SSH in and install

```bash
ssh -i your-key.pem ec2-user@YOUR_IP
curl -fsSL https://raw.githubusercontent.com/oddgames/ui-automation/main/server/install.sh | bash
```

---

## Option B: AWS CLI (One-liner from your terminal)

Prerequisites: [AWS CLI](https://aws.amazon.com/cli/) installed and configured (`aws configure`).

### 1. Create a key pair (skip if you already have one)

```bash
aws ec2 create-key-pair --key-name uiat-key --query "KeyMaterial" --output text > uiat-key.pem
chmod 400 uiat-key.pem
```

### 2. Create a security group

```bash
# Create security group
SG_ID=$(aws ec2 create-security-group \
  --group-name uiat-server \
  --description "UIAutomation Test Server" \
  --query "GroupId" --output text)

# Allow SSH and HTTP
aws ec2 authorize-security-group-ingress --group-id $SG_ID --protocol tcp --port 22 --cidr 0.0.0.0/0
aws ec2 authorize-security-group-ingress --group-id $SG_ID --protocol tcp --port 80 --cidr 0.0.0.0/0
```

### 3. Launch the instance and install

```bash
# Launch (Amazon Linux 2023, t3.small, 50GB)
INSTANCE_ID=$(aws ec2 run-instances \
  --image-id resolve:ssm:/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-x86_64 \
  --instance-type t3.small \
  --key-name uiat-key \
  --security-group-ids $SG_ID \
  --block-device-mappings '[{"DeviceName":"/dev/xvda","Ebs":{"VolumeSize":50,"VolumeType":"gp3"}}]' \
  --tag-specifications 'ResourceType=instance,Tags=[{Key=Name,Value=uiat-server}]' \
  --query "Instances[0].InstanceId" --output text)

# Wait for it to be running
aws ec2 wait instance-running --instance-ids $INSTANCE_ID

# Get the public IP
IP=$(aws ec2 describe-instances --instance-ids $INSTANCE_ID \
  --query "Reservations[0].Instances[0].PublicIpAddress" --output text)

echo "Instance ready at $IP"

# SSH in and install (wait ~30s for SSH to be ready)
sleep 30
ssh -o StrictHostKeyChecking=no -i uiat-key.pem ec2-user@$IP \
  "curl -fsSL https://raw.githubusercontent.com/oddgames/ui-automation/main/server/install.sh | bash"
```

Server is running at `http://$IP`.

---

Auto-updates from GitHub every 5 minutes — just push to `main`.

## Expanding Storage

In the AWS Console: **EC2 > Volumes > Select volume > Modify > Change size**

Then SSH in and run:
```bash
sudo growpart /dev/xvda 1 && sudo xfs_growfs /
```

Storage is ~$0.08/GB/month (100 GB = $8/mo, 500 GB = $40/mo).

## Bulk Delete (Disk Space Management)

The web dashboard has a **Bulk Delete** button. Filter by age, project, or result.

API for automation:
```bash
# Delete all passing tests older than 30 days
curl -X DELETE "http://YOUR_SERVER/api/sessions/bulk?olderThan=2025-01-01&result=pass"
```

## Monitoring

```bash
# Health check
curl http://YOUR_SERVER/api/health

# Storage usage
curl http://YOUR_SERVER/api/storage

# Server logs
ssh -i your-key.pem ec2-user@YOUR_IP "cd /opt/uiat/server && docker compose logs -f"

# Auto-update logs
ssh -i your-key.pem ec2-user@YOUR_IP "tail -f /var/log/uiat-update.log"
```

## Cost

| Component | Monthly |
|-----------|---------|
| t3.micro (free tier 1yr) | $0 |
| t3.small | ~$15 |
| 50 GB storage | ~$4 |
| Data transfer (100GB) | $0 |

**Typical: ~$15-20/month**
